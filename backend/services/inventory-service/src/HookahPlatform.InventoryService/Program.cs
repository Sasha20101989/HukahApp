using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.InventoryService.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Net.Http.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("inventory-service");
builder.AddPostgresDbContext<InventoryDbContext>();
builder.Services.AddHostedService<InventoryRabbitMqConsumer>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<InventoryDbContext>("inventory-service");

app.MapGet("/api/inventory", async (Guid? branchId, bool? lowStockOnly, InventoryDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.InventoryItems.AsNoTracking();
    if (branchId is not null)
    {
        query = query.Where(item => item.BranchId == branchId);
    }
    if (lowStockOnly == true)
    {
        query = query.Where(item => item.StockGrams < item.MinStockGrams);
    }

    return Results.Ok(await query.OrderBy(item => item.BranchId).ThenBy(item => item.TobaccoId).ToListAsync(cancellationToken));
});

app.MapPost("/api/inventory/check", async (InventoryAvailabilityRequest request, InventoryDbContext db, CancellationToken cancellationToken) =>
{
    if (request.Items.Count == 0)
    {
        return HttpResults.Validation("Availability check must contain at least one tobacco item.");
    }

    var tobaccoIds = request.Items.Select(item => item.TobaccoId).ToArray();
    var stockByTobaccoId = await db.InventoryItems
        .AsNoTracking()
        .Where(item => item.BranchId == request.BranchId && tobaccoIds.Contains(item.TobaccoId))
        .ToDictionaryAsync(item => item.TobaccoId, item => item.StockGrams, cancellationToken);

    var checkedItems = request.Items.Select(required =>
    {
        var stock = stockByTobaccoId.TryGetValue(required.TobaccoId, out var stockGrams) ? stockGrams : 0m;

        return new InventoryAvailabilityItem(
            required.TobaccoId,
            required.RequiredGrams,
            stock,
            stock >= required.RequiredGrams,
            Math.Max(0, required.RequiredGrams - stock));
    }).ToArray();

    return Results.Ok(new InventoryAvailabilityResponse(
        request.BranchId,
        checkedItems.All(item => item.IsAvailable),
        checkedItems));
});

app.MapPost("/api/inventory/in", async (InventoryInRequest request, InventoryDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    if (request.BranchId == Guid.Empty || request.TobaccoId == Guid.Empty) return HttpResults.Validation("Branch and tobacco are required.");
    if (request.AmountGrams <= 0) return HttpResults.Validation("Incoming amount must be positive.");
    if (request.CostPerGram < 0) return HttpResults.Validation("Cost per gram cannot be negative.");

    var item = await GetOrCreateItemAsync(db, request.BranchId, request.TobaccoId, cancellationToken);
    item.StockGrams += request.AmountGrams;
    item.UpdatedAt = DateTimeOffset.UtcNow;

    var reason = !string.IsNullOrWhiteSpace(request.Comment) ? request.Comment.Trim() : !string.IsNullOrWhiteSpace(request.Supplier) ? $"Supplier: {request.Supplier.Trim()}" : "Inventory receipt";
    db.InventoryMovements.Add(new InventoryMovementEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, TobaccoId = request.TobaccoId, Type = "IN", AmountGrams = request.AmountGrams, Reason = reason, OrderId = null, CreatedBy = null, CreatedAt = DateTimeOffset.UtcNow });
    var outboxEvents = BuildLowStockEvents(item);
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    return Results.Ok(item);
});

app.MapPost("/api/inventory/out", async (InventoryOutRequest request, InventoryDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    if (request.BranchId == Guid.Empty || request.TobaccoId == Guid.Empty) return HttpResults.Validation("Branch and tobacco are required.");
    if (request.AmountGrams <= 0) return HttpResults.Validation("Write-off amount must be positive.");
    var reason = request.Reason?.Trim();
    if (string.IsNullOrWhiteSpace(reason)) return HttpResults.Validation("Write-off reason is required.");

    var item = await GetOrCreateItemAsync(db, request.BranchId, request.TobaccoId, cancellationToken);
    if (item.StockGrams < request.AmountGrams)
    {
        return HttpResults.Conflict("Not enough tobacco in stock.");
    }

    item.StockGrams -= request.AmountGrams;
    item.UpdatedAt = DateTimeOffset.UtcNow;

    db.InventoryMovements.Add(new InventoryMovementEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, TobaccoId = request.TobaccoId, Type = "OUT", AmountGrams = request.AmountGrams, Reason = reason, OrderId = request.OrderId, CreatedBy = request.CreatedBy, CreatedAt = DateTimeOffset.UtcNow });
    var outboxEvents = new List<IIntegrationEvent>
    {
        new InventoryWrittenOff(request.BranchId, request.TobaccoId, request.OrderId, request.AmountGrams, DateTimeOffset.UtcNow)
    };
    outboxEvents.AddRange(BuildLowStockEvents(item));
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    return Results.Ok(item);
});

app.MapPost("/api/inventory/adjustment", async (InventoryAdjustmentRequest request, InventoryDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    if (request.BranchId == Guid.Empty || request.TobaccoId == Guid.Empty) return HttpResults.Validation("Branch and tobacco are required.");
    if (request.NewStockGrams < 0) return HttpResults.Validation("New stock amount cannot be negative.");

    var item = await GetOrCreateItemAsync(db, request.BranchId, request.TobaccoId, cancellationToken);
    var delta = request.NewStockGrams - item.StockGrams;
    item.StockGrams = request.NewStockGrams;
    item.UpdatedAt = DateTimeOffset.UtcNow;

    db.InventoryMovements.Add(new InventoryMovementEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, TobaccoId = request.TobaccoId, Type = "ADJUSTMENT", AmountGrams = delta, Reason = "Manual adjustment", OrderId = null, CreatedBy = request.CreatedBy, CreatedAt = DateTimeOffset.UtcNow });
    var outboxEvents = BuildLowStockEvents(item);
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    return Results.Ok(item);
});

app.MapPatch("/api/inventory/{id:guid}", async (Guid id, UpdateInventoryItemRequest request, InventoryDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var item = await db.InventoryItems.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (item is null) return HttpResults.NotFound("Inventory item", id);
    if (request.MinStockGrams is not null && request.MinStockGrams < 0) return HttpResults.Validation("Minimum stock cannot be negative.");

    if (request.MinStockGrams is not null)
    {
        item.MinStockGrams = request.MinStockGrams.Value;
    }

    item.UpdatedAt = DateTimeOffset.UtcNow;
    var outboxEvents = BuildLowStockEvents(item);
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    return Results.Ok(item);
});

app.MapGet("/api/inventory/movements", async (Guid? branchId, Guid? tobaccoId, DateOnly? from, DateOnly? to, InventoryDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.InventoryMovements.AsNoTracking();
    if (branchId is not null)
    {
        query = query.Where(movement => movement.BranchId == branchId);
    }
    if (tobaccoId is not null)
    {
        query = query.Where(movement => movement.TobaccoId == tobaccoId);
    }
    if (from is not null)
    {
        query = query.Where(movement => DateOnly.FromDateTime(movement.CreatedAt.UtcDateTime) >= from);
    }
    if (to is not null)
    {
        query = query.Where(movement => DateOnly.FromDateTime(movement.CreatedAt.UtcDateTime) <= to);
    }

    return Results.Ok(await query.OrderByDescending(movement => movement.CreatedAt).ToListAsync(cancellationToken));
});

app.MapPost("/api/inventory/dispatch-event", async (InventoryEventRequest request, InventoryDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (request.Event != nameof(OrderServed)) return HttpResults.Validation($"No inventory handler for event '{request.Event}'.");
    var served = new OrderServed(request.OrderId, request.BranchId, request.MixId, request.BowlId, request.OccurredAt, request.OccurredAt) { EventId = request.EventId };
    return await InventoryEventProcessor.ApplyOrderServedAsync(served, db, events, httpClientFactory, configuration, cancellationToken);
});

app.Run();

static async Task<InventoryItemEntity> GetOrCreateItemAsync(InventoryDbContext db, Guid branchId, Guid tobaccoId, CancellationToken cancellationToken)
{
    var item = await db.InventoryItems.FirstOrDefaultAsync(candidate => candidate.BranchId == branchId && candidate.TobaccoId == tobaccoId, cancellationToken);
    if (item is not null)
    {
        return item;
    }

    item = new InventoryItemEntity { Id = Guid.NewGuid(), BranchId = branchId, TobaccoId = tobaccoId, StockGrams = 0, MinStockGrams = 50, UpdatedAt = DateTimeOffset.UtcNow };
    db.InventoryItems.Add(item);
    return item;
}

static IReadOnlyCollection<IIntegrationEvent> BuildLowStockEvents(InventoryItemEntity item)
{
    if (item.StockGrams < item.MinStockGrams)
    {
        return [new LowStockDetected(item.BranchId, item.TobaccoId, item.StockGrams, item.MinStockGrams, DateTimeOffset.UtcNow)];
    }

    return [];
}

static class InventoryEventProcessor
{
    public static async Task<IResult> ApplyOrderServedAsync(OrderServed served, InventoryDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (await db.ProcessedEvents.AnyAsync(item => item.Handler == "inventory-service" && item.EventId == served.EventId, cancellationToken))
        {
            return Results.Accepted("/api/inventory/movements", new { served.EventId, duplicate = true });
        }

        var mix = await GetMixAsync(served.MixId, httpClientFactory, configuration, cancellationToken);
        if (mix is null) return HttpResults.Validation($"Mix '{served.MixId}' was not found.");
        if (mix.BowlId != served.BowlId) return HttpResults.Validation("OrderServed bowl does not match mix bowl.");

        var outboxEvents = new List<IIntegrationEvent>();
        foreach (var mixItem in mix.Items)
        {
            var alreadyWrittenOff = await db.InventoryMovements.AnyAsync(movement =>
                movement.OrderId == served.OrderId &&
                movement.TobaccoId == mixItem.TobaccoId &&
                movement.Type == "OUT",
                cancellationToken);
            if (alreadyWrittenOff) continue;

            var item = await GetOrCreateItemAsync(db, served.BranchId, mixItem.TobaccoId, cancellationToken);
            if (item.StockGrams < mixItem.Grams) return HttpResults.Conflict($"Not enough tobacco '{mixItem.TobaccoId}' in stock.");

            item.StockGrams -= mixItem.Grams;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            db.InventoryMovements.Add(new InventoryMovementEntity { Id = Guid.NewGuid(), BranchId = served.BranchId, TobaccoId = mixItem.TobaccoId, Type = "OUT", AmountGrams = mixItem.Grams, Reason = "Order served event", OrderId = served.OrderId, CreatedBy = null, CreatedAt = DateTimeOffset.UtcNow });
            outboxEvents.Add(new InventoryWrittenOff(served.BranchId, mixItem.TobaccoId, served.OrderId, mixItem.Grams, DateTimeOffset.UtcNow));
            outboxEvents.AddRange(BuildLowStockEvents(item));
        }

        db.ProcessedEvents.Add(new ProcessedIntegrationEventEntity { Handler = "inventory-service", EventId = served.EventId, ProcessedAt = DateTimeOffset.UtcNow });
        var outboxMessages = db.AddOutboxMessages(outboxEvents);
        await db.SaveChangesAsync(cancellationToken);
        await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);
        return Results.Accepted("/api/inventory/movements", new { served.EventId, writtenOffItems = outboxEvents.OfType<InventoryWrittenOff>().Count() });
    }

    private static async Task<InventoryItemEntity> GetOrCreateItemAsync(InventoryDbContext db, Guid branchId, Guid tobaccoId, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(candidate => candidate.BranchId == branchId && candidate.TobaccoId == tobaccoId, cancellationToken);
        if (item is not null) return item;

        item = new InventoryItemEntity { Id = Guid.NewGuid(), BranchId = branchId, TobaccoId = tobaccoId, StockGrams = 0, MinStockGrams = 50, UpdatedAt = DateTimeOffset.UtcNow };
        db.InventoryItems.Add(item);
        return item;
    }

    private static IReadOnlyCollection<IIntegrationEvent> BuildLowStockEvents(InventoryItemEntity item)
    {
        return item.StockGrams < item.MinStockGrams
            ? [new LowStockDetected(item.BranchId, item.TobaccoId, item.StockGrams, item.MinStockGrams, DateTimeOffset.UtcNow)]
            : [];
    }

    private static async Task<MixDto?> GetMixAsync(Guid mixId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var mixologyBaseUrl = configuration["Services:mixology-service:BaseUrl"] ?? "http://mixology-service:8080";
        var client = httpClientFactory.CreateClient("inventory-service");
        return await client.GetFromJsonAsync<MixDto>($"{mixologyBaseUrl}/api/mixes/{mixId}", cancellationToken);
    }
}

sealed class InventoryRabbitMqConsumer(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<InventoryRabbitMqConsumer> logger) : BackgroundService
{
    private readonly bool _enabled = configuration.GetValue("RabbitMQ:Consumers:Enabled", false);
    private readonly string _queue = configuration["RabbitMQ:Consumers:InventoryQueue"] ?? "inventory.order-served";
    private readonly string _exchange = configuration["RabbitMQ:Exchange"] ?? MessagingCatalog.Exchange;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) return;

        var factory = CreateFactory(configuration);
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(_exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_queue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = MessagingCatalog.DeadLetterExchange }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(_queue, _exchange, "order.served", cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var eventName = args.BasicProperties.Type;
                if (string.IsNullOrWhiteSpace(eventName) && args.BasicProperties.Headers?.TryGetValue("event_name", out var header) == true) eventName = ReadHeaderAsString(header);
                var message = new OutboxMessage { EventName = eventName ?? string.Empty, Payload = Encoding.UTF8.GetString(args.Body.Span) };
                var integrationEvent = OutboxMessageSerializer.Deserialize(message);
                if (integrationEvent is not OrderServed served)
                {
                    if (integrationEvent is null) throw new InvalidOperationException($"Invalid inventory event '{eventName}'.");
                    logger.LogDebug("Skipping unsupported inventory event '{EventName}'.", eventName);
                    await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: args.CancellationToken);
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
                var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                var result = await InventoryEventProcessor.ApplyOrderServedAsync(served, db, events, httpClientFactory, configuration, args.CancellationToken);
                if (result is IStatusCodeHttpResult { StatusCode: >= 400 }) throw new InvalidOperationException($"Inventory event '{eventName}' was rejected.");
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: args.CancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ inventory event handling failed.");
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken: args.CancellationToken);
            }
        };

        await channel.BasicConsumeAsync(_queue, autoAck: false, consumer, cancellationToken: stoppingToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private static ConnectionFactory CreateFactory(IConfiguration configuration) => new()
    {
        HostName = configuration["RabbitMQ:HostName"] ?? "localhost",
        Port = configuration.GetValue("RabbitMQ:Port", 5672),
        UserName = configuration["RabbitMQ:UserName"] ?? ConnectionFactory.DefaultUser,
        Password = configuration["RabbitMQ:Password"] ?? ConnectionFactory.DefaultPass,
        VirtualHost = configuration["RabbitMQ:VirtualHost"] ?? ConnectionFactory.DefaultVHost,
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        ClientProvidedName = "inventory-service"
    };

    private static string? ReadHeaderAsString(object? header)
    {
        return header switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string value => value,
            _ => header?.ToString()
        };
    }
}

public sealed record InventoryAvailabilityRequest(Guid BranchId, IReadOnlyCollection<InventoryAvailabilityRequestItem> Items);
public sealed record InventoryAvailabilityRequestItem(Guid TobaccoId, decimal RequiredGrams);
public sealed record InventoryAvailabilityResponse(Guid BranchId, bool IsAvailable, IReadOnlyCollection<InventoryAvailabilityItem> Items);
public sealed record InventoryAvailabilityItem(Guid TobaccoId, decimal RequiredGrams, decimal StockGrams, bool IsAvailable, decimal ShortageGrams);
public sealed record InventoryInRequest(Guid BranchId, Guid TobaccoId, decimal AmountGrams, decimal CostPerGram, string? Supplier, string? Comment);
public sealed record InventoryOutRequest(Guid BranchId, Guid TobaccoId, decimal AmountGrams, string Reason, Guid? OrderId, Guid? CreatedBy);
public sealed record InventoryAdjustmentRequest(Guid BranchId, Guid TobaccoId, decimal NewStockGrams, Guid? CreatedBy);
public sealed record UpdateInventoryItemRequest(decimal? MinStockGrams);
public sealed record MixDto(Guid Id, Guid BowlId, IReadOnlyCollection<MixItemDto> Items);
public sealed record MixItemDto(Guid Id, Guid TobaccoId, decimal Percent, decimal Grams);
