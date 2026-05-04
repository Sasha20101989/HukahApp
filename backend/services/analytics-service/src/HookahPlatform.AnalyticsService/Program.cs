using HookahPlatform.AnalyticsService.Persistence;
using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("analytics-service");
builder.AddPostgresDbContext<AnalyticsDbContext>();
builder.Services.AddHostedService<AnalyticsRabbitMqConsumer>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<AnalyticsDbContext>("analytics-service");

app.MapPost("/api/analytics/events", async (AnalyticsEventEnvelope envelope, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var result = await AnalyticsEventProcessor.ApplyAsync(envelope, db, cancellationToken);
    return result ?? Results.Accepted($"/api/analytics/events/{envelope.Event}", envelope);
});

app.MapGet("/api/analytics/dashboard", async (Guid? branchId, DateOnly from, DateOnly to, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var fromDate = from.ToDateTime(TimeOnly.MinValue);
    var toDate = to.ToDateTime(TimeOnly.MaxValue);
    var scopedOrders = await db.Orders.AsNoTracking().Where(order => (branchId == null || order.BranchId == branchId) && order.CreatedAt.UtcDateTime >= fromDate && order.CreatedAt.UtcDateTime <= toDate).ToListAsync(cancellationToken);
    var scopedBookings = await db.Bookings.AsNoTracking().Where(booking => (branchId == null || booking.BranchId == branchId) && booking.CreatedAt.UtcDateTime >= fromDate && booking.CreatedAt.UtcDateTime <= toDate).ToListAsync(cancellationToken);
    var revenue = scopedOrders.Where(order => order.Status is OrderStatuses.Served or OrderStatuses.Smoking or OrderStatuses.Completed).Sum(order => order.TotalPrice);
    var averageCheck = scopedOrders.Count == 0 ? 0 : Math.Round(revenue / scopedOrders.Count, 2);
    var noShowCount = scopedBookings.Count(booking => booking.Status == BookingStatuses.NoShow);
    var noShowRate = scopedBookings.Count == 0 ? 0 : Math.Round(noShowCount * 100m / scopedBookings.Count, 2);
    return Results.Ok(new DashboardMetrics(revenue, scopedOrders.Count, averageCheck, scopedBookings.Count, noShowRate, from, to, branchId));
});

app.MapGet("/api/analytics/top-mixes", async (Guid? branchId, DateOnly? from, DateOnly? to, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var query = AnalyticsQueries.FilterOrders(db.Orders.AsNoTracking(), branchId, from, to);
    return Results.Ok(await query.GroupBy(order => order.MixId).Select(group => new TopMix(group.Key, $"Mix {group.Key.ToString().Substring(0, 8)}", group.Count(), 0, group.Sum(order => order.TotalPrice))).OrderByDescending(metric => metric.OrdersCount).Take(20).ToListAsync(cancellationToken));
});

app.MapGet("/api/analytics/tobacco-usage", async (Guid? branchId, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.TobaccoUsage.AsNoTracking();
    if (branchId is not null) query = query.Where(item => item.BranchId == branchId);
    return Results.Ok(await query.OrderByDescending(item => item.Grams).Select(item => new TobaccoUsage(item.BranchId, item.TobaccoId, item.Grams)).ToListAsync(cancellationToken));
});

app.MapGet("/api/analytics/staff-performance", async (Guid? branchId, DateOnly? from, DateOnly? to, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var query = AnalyticsQueries.FilterOrders(db.Orders.AsNoTracking(), branchId, from, to);
    return Results.Ok(await query.Where(order => order.HookahMasterId != null).GroupBy(order => order.HookahMasterId!.Value).Select(group => new StaffPerformance(group.Key, $"Staff {group.Key.ToString().Substring(0, 8)}", group.Count(), 0, TimeSpan.Zero)).OrderByDescending(metric => metric.OrdersServed).ToListAsync(cancellationToken));
});

app.MapGet("/api/analytics/table-load", async (Guid? branchId, DateOnly? from, DateOnly? to, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var query = AnalyticsQueries.FilterBookings(db.Bookings.AsNoTracking(), branchId, from, to);
    return Results.Ok(await query.GroupBy(booking => new { booking.BranchId, booking.TableId }).Select(group => new TableLoad(group.Key.BranchId, group.Key.TableId, $"Table {group.Key.TableId.ToString().Substring(0, 8)}", Math.Min(100, group.Count() * 5m))).OrderByDescending(metric => metric.LoadPercent).ToListAsync(cancellationToken));
});

app.Run();

static class AnalyticsEventProcessor
{
    public static async Task<IResult?> ApplyAsync(AnalyticsEventEnvelope envelope, AnalyticsDbContext db, CancellationToken cancellationToken)
    {
        if (await db.ProcessedEvents.AnyAsync(item => item.Handler == "analytics-service" && item.EventId == envelope.EventId, cancellationToken))
        {
            return Results.Accepted($"/api/analytics/events/{envelope.Event}", new { envelope.EventId, duplicate = true });
        }

        switch (envelope.Event)
        {
            case nameof(OrderCreated):
                if (envelope.OrderId is null || envelope.BranchId is null || envelope.TableId is null || envelope.MixId is null) return HttpResults.Validation("OrderCreated analytics event requires orderId, branchId, tableId and mixId.");
                if (!await db.Orders.AnyAsync(candidate => candidate.Id == envelope.OrderId.Value, cancellationToken))
                {
                    db.Orders.Add(new AnalyticsOrderEntity { Id = envelope.OrderId.Value, BranchId = envelope.BranchId.Value, TableId = envelope.TableId.Value, MixId = envelope.MixId.Value, HookahMasterId = envelope.HookahMasterId, TotalPrice = envelope.TotalPrice ?? 0, Status = OrderStatuses.New, CreatedAt = envelope.OccurredAt });
                }
                break;
            case nameof(OrderStatusChanged):
                if (envelope.OrderId is null || string.IsNullOrWhiteSpace(envelope.Status)) return HttpResults.Validation("OrderStatusChanged analytics event requires orderId and status.");
                var order = await db.Orders.FirstOrDefaultAsync(candidate => candidate.Id == envelope.OrderId.Value, cancellationToken);
                if (order is not null) order.Status = envelope.Status;
                break;
            case nameof(InventoryWrittenOff):
                if (envelope.BranchId is null || envelope.TobaccoId is null || envelope.AmountGrams is null) return HttpResults.Validation("InventoryWrittenOff analytics event requires branchId, tobaccoId and amountGrams.");
                var usage = await db.TobaccoUsage.FirstOrDefaultAsync(candidate => candidate.BranchId == envelope.BranchId.Value && candidate.TobaccoId == envelope.TobaccoId.Value, cancellationToken);
                if (usage is null)
                {
                    usage = new AnalyticsTobaccoUsageEntity { BranchId = envelope.BranchId.Value, TobaccoId = envelope.TobaccoId.Value, Grams = 0 };
                    db.TobaccoUsage.Add(usage);
                }
                usage.Grams += envelope.AmountGrams.Value;
                break;
            case nameof(BookingCreated):
                if (envelope.BookingId is null || envelope.BranchId is null || envelope.TableId is null) return HttpResults.Validation("BookingCreated analytics event requires bookingId, branchId and tableId.");
                if (!await db.Bookings.AnyAsync(candidate => candidate.Id == envelope.BookingId.Value, cancellationToken))
                {
                    db.Bookings.Add(new AnalyticsBookingEntity { Id = envelope.BookingId.Value, BranchId = envelope.BranchId.Value, TableId = envelope.TableId.Value, Status = BookingStatuses.New, StartTime = envelope.StartTime, EndTime = envelope.EndTime, CreatedAt = envelope.OccurredAt });
                }
                break;
            case nameof(BookingConfirmed):
            case nameof(BookingCancelled):
                if (envelope.BookingId is null) return HttpResults.Validation($"{envelope.Event} analytics event requires bookingId.");
                var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == envelope.BookingId.Value, cancellationToken);
                if (booking is not null) booking.Status = envelope.Event == nameof(BookingConfirmed) ? BookingStatuses.Confirmed : BookingStatuses.Cancelled;
                break;
        }

        db.ProcessedEvents.Add(new ProcessedIntegrationEventEntity { Handler = "analytics-service", EventId = envelope.EventId, ProcessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public static AnalyticsEventEnvelope? ToEnvelope(IIntegrationEvent integrationEvent)
    {
        return integrationEvent switch
        {
            BookingCreated e => new(e.EventId, nameof(BookingCreated), e.OccurredAt, null, e.BookingId, e.BranchId, e.TableId, null, null, null, null, null, null, null, e.StartTime, e.EndTime),
            BookingConfirmed e => new(e.EventId, nameof(BookingConfirmed), e.OccurredAt, null, e.BookingId, null, null, null, null, null, null, null, null, null, null, null),
            BookingCancelled e => new(e.EventId, nameof(BookingCancelled), e.OccurredAt, null, e.BookingId, null, null, null, null, null, null, null, null, null, null, null),
            OrderCreated e => new(e.EventId, nameof(OrderCreated), e.OccurredAt, e.OrderId, null, e.BranchId, e.TableId, e.MixId, null, null, null, null, null, null, null, null),
            OrderStatusChanged e => new(e.EventId, nameof(OrderStatusChanged), e.OccurredAt, e.OrderId, null, null, null, null, null, null, null, null, null, e.Status, null, null),
            InventoryWrittenOff e => new(e.EventId, nameof(InventoryWrittenOff), e.OccurredAt, e.OrderId, null, e.BranchId, null, null, e.TobaccoId, null, null, e.AmountGrams, null, null, null, null),
            ReviewCreated e => new(e.EventId, nameof(ReviewCreated), e.OccurredAt, null, null, null, null, e.MixId, null, null, null, null, e.Rating, null, null, null),
            _ => null
        };
    }
}

sealed class AnalyticsRabbitMqConsumer(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AnalyticsRabbitMqConsumer> logger) : BackgroundService
{
    private readonly bool _enabled = configuration.GetValue("RabbitMQ:Consumers:Enabled", false);
    private readonly string _queue = configuration["RabbitMQ:Consumers:AnalyticsQueue"] ?? "analytics.events";
    private readonly string _exchange = configuration["RabbitMQ:Exchange"] ?? MessagingCatalog.Exchange;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) return;

        var factory = CreateFactory(configuration);
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(_exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_queue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = MessagingCatalog.DeadLetterExchange }, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(_queue, _exchange, "#", cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var eventName = args.BasicProperties.Type;
                if (string.IsNullOrWhiteSpace(eventName) && args.BasicProperties.Headers?.TryGetValue("event_name", out var header) == true) eventName = ReadHeaderAsString(header);
                var message = new OutboxMessage { EventName = eventName ?? string.Empty, Payload = Encoding.UTF8.GetString(args.Body.Span) };
                var integrationEvent = OutboxMessageSerializer.Deserialize(message);
                var envelope = integrationEvent is null ? null : AnalyticsEventProcessor.ToEnvelope(integrationEvent);
                if (integrationEvent is null) throw new InvalidOperationException($"Invalid analytics event '{eventName}'.");
                if (envelope is null)
                {
                    logger.LogDebug("Skipping unsupported analytics event '{EventName}'.", eventName);
                    await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: args.CancellationToken);
                    return;
                }
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                var result = await AnalyticsEventProcessor.ApplyAsync(envelope, db, args.CancellationToken);
                if (result is IStatusCodeHttpResult { StatusCode: >= 400 }) throw new InvalidOperationException($"Analytics event '{eventName}' was rejected.");
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: args.CancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ analytics event handling failed.");
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
        ClientProvidedName = "analytics-service"
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

public sealed record DashboardMetrics(decimal Revenue, int OrdersCount, decimal AverageCheck, int BookingsCount, decimal NoShowRate, DateOnly From, DateOnly To, Guid? BranchId);

static class AnalyticsQueries
{
    public static IQueryable<AnalyticsOrderEntity> FilterOrders(IQueryable<AnalyticsOrderEntity> query, Guid? branchId, DateOnly? from, DateOnly? to)
    {
        if (branchId is not null) query = query.Where(order => order.BranchId == branchId);
        if (from is not null)
        {
            var fromDate = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(order => order.CreatedAt.UtcDateTime >= fromDate);
        }
        if (to is not null)
        {
            var toDate = to.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(order => order.CreatedAt.UtcDateTime <= toDate);
        }
        return query;
    }

    public static IQueryable<AnalyticsBookingEntity> FilterBookings(IQueryable<AnalyticsBookingEntity> query, Guid? branchId, DateOnly? from, DateOnly? to)
    {
        if (branchId is not null) query = query.Where(booking => booking.BranchId == branchId);
        if (from is not null)
        {
            var fromDate = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(booking => booking.CreatedAt.UtcDateTime >= fromDate);
        }
        if (to is not null)
        {
            var toDate = to.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(booking => booking.CreatedAt.UtcDateTime <= toDate);
        }
        return query;
    }
}

public sealed record TopMix(Guid MixId, string Name, int OrdersCount, decimal Rating, decimal Revenue);
public sealed record TobaccoUsage(Guid BranchId, Guid TobaccoId, decimal Grams);
public sealed record StaffPerformance(Guid StaffId, string StaffName, int OrdersServed, decimal Rating, TimeSpan AveragePrepareTime);
public sealed record TableLoad(Guid BranchId, Guid TableId, string TableName, decimal LoadPercent);
public sealed record AnalyticsEventEnvelope(Guid EventId, string Event, DateTimeOffset OccurredAt, Guid? OrderId, Guid? BookingId, Guid? BranchId, Guid? TableId, Guid? MixId, Guid? TobaccoId, Guid? HookahMasterId, decimal? TotalPrice, decimal? AmountGrams, int? Rating, string? Status, DateTimeOffset? StartTime, DateTimeOffset? EndTime);
