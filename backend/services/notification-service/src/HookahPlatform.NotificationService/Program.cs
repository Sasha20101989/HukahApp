using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using HookahPlatform.NotificationService.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("notification-service");
builder.AddPostgresDbContext<NotificationDbContext>();
builder.Services.AddHostedService<NotificationRabbitMqConsumer>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<NotificationDbContext>("notification-service");

app.MapGet("/api/notifications", async (Guid? userId, bool? unreadOnly, NotificationDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Notifications.AsNoTracking();
    if (userId is not null) query = query.Where(notification => notification.UserId == userId);
    if (unreadOnly == true) query = query.Where(notification => notification.ReadAt == null);
    return Results.Ok(await query.OrderByDescending(notification => notification.CreatedAt).ToListAsync(cancellationToken));
});

app.MapGet("/api/notifications/templates", async (NotificationDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Templates.AsNoTracking().OrderBy(template => template.Code).ToListAsync(cancellationToken)));

app.MapGet("/api/notifications/preferences/{userId:guid}", async (Guid userId, NotificationDbContext db, CancellationToken cancellationToken) =>
{
    var preference = await NotificationEventProcessor.GetOrCreatePreferenceAsync(userId, db, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(preference);
});

app.MapPut("/api/notifications/preferences/{userId:guid}", async (Guid userId, UpdateNotificationPreferenceRequest request, NotificationDbContext db, CancellationToken cancellationToken) =>
{
    var preference = await db.Preferences.FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
    if (preference is null)
    {
        preference = new NotificationPreferenceEntity { UserId = userId };
        db.Preferences.Add(preference);
    }
    preference.CrmEnabled = request.CrmEnabled;
    preference.TelegramEnabled = request.TelegramEnabled;
    preference.SmsEnabled = request.SmsEnabled;
    preference.EmailEnabled = request.EmailEnabled;
    preference.PushEnabled = request.PushEnabled;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(preference);
});

app.MapPatch("/api/notifications/{id:guid}/read", async (Guid id, NotificationDbContext db, CancellationToken cancellationToken) =>
{
    var notification = await db.Notifications.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (notification is null) return HttpResults.NotFound("Notification", id);
    notification.ReadAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(notification);
});

app.MapPost("/api/notifications/send", async (SendNotificationRequest request, NotificationDbContext db, CancellationToken cancellationToken) =>
{
    if (!await NotificationEventProcessor.ChannelIsAllowedAsync(request.UserId, request.Channel, db, cancellationToken)) return HttpResults.Validation($"Channel '{request.Channel}' is disabled for this user.");
    var notification = new NotificationEntity { Id = Guid.NewGuid(), UserId = request.UserId, Channel = request.Channel, Title = request.Title, Message = request.Message, Metadata = JsonSerializer.Serialize(request.Metadata), CreatedAt = DateTimeOffset.UtcNow, ReadAt = null };
    db.Notifications.Add(notification);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/notifications/{notification.Id}", notification);
});

app.MapPost("/api/notifications/dispatch-event", async (NotificationEventRequest request, NotificationDbContext db, CancellationToken cancellationToken) =>
{
    var result = await NotificationEventProcessor.ApplyAsync(request, db, cancellationToken);
    return result;
});

app.Run();

static class NotificationEventProcessor
{
    public static async Task<NotificationPreferenceEntity> GetOrCreatePreferenceAsync(Guid userId, NotificationDbContext db, CancellationToken cancellationToken)
    {
        var preference = await db.Preferences.FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        if (preference is not null) return preference;
        preference = new NotificationPreferenceEntity { UserId = userId, CrmEnabled = true, TelegramEnabled = true, SmsEnabled = true, EmailEnabled = true, PushEnabled = true };
        db.Preferences.Add(preference);
        return preference;
    }

    public static async Task<bool> ChannelIsAllowedAsync(Guid userId, string channel, NotificationDbContext db, CancellationToken cancellationToken)
    {
        var preference = await GetOrCreatePreferenceAsync(userId, db, cancellationToken);
        return channel.ToUpperInvariant() switch
        {
            "CRM" => preference.CrmEnabled,
            "TELEGRAM" => preference.TelegramEnabled,
            "SMS" => preference.SmsEnabled,
            "EMAIL" => preference.EmailEnabled,
            "PUSH" => preference.PushEnabled,
            _ => false
        };
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;
        foreach (var (key, value) in values) result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    public static async Task<IResult> ApplyAsync(NotificationEventRequest request, NotificationDbContext db, CancellationToken cancellationToken)
    {
        if (await db.ProcessedEvents.AnyAsync(item => item.Handler == "notification-service" && item.EventId == request.EventId, cancellationToken))
        {
            return Results.Accepted("/api/notifications", new { request.EventId, duplicate = true });
        }

        var templateCode = request.Event switch
        {
            nameof(BookingCreated) => "booking.created.manager",
            nameof(BookingConfirmed) => "booking.confirmed.client",
            nameof(BookingCancelled) => "booking.cancelled.manager",
            nameof(PaymentSucceeded) => "payment.succeeded.client",
            nameof(PaymentFailed) => "payment.failed.manager",
            nameof(PaymentRefunded) => "payment.refunded.client",
            nameof(LowStockDetected) => "inventory.low-stock.manager",
            nameof(OrderServed) => "order.served.coal-timer",
            _ => null
        };

        if (templateCode is null) return HttpResults.Validation($"No notification template for event '{request.Event}'.");
        var template = await db.Templates.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Code == templateCode, cancellationToken);
        if (template is null) return HttpResults.Validation($"No notification template for event '{request.Event}'.");

        IReadOnlyCollection<Guid> userIds = request.UserIds.Count == 0 ? [Guid.Parse("90000000-0000-0000-0000-000000000001")] : request.UserIds;
        var created = new List<NotificationEntity>();
        foreach (var userId in userIds)
        {
            if (!await ChannelIsAllowedAsync(userId, template.Channel, db, cancellationToken)) continue;
            var notification = new NotificationEntity { Id = Guid.NewGuid(), UserId = userId, Channel = template.Channel, Title = Render(template.Title, request.Values), Message = Render(template.Message, request.Values), Metadata = JsonSerializer.Serialize(request.Values), CreatedAt = DateTimeOffset.UtcNow, ReadAt = null };
            db.Notifications.Add(notification);
            created.Add(notification);
        }
        db.ProcessedEvents.Add(new ProcessedIntegrationEventEntity { Handler = "notification-service", EventId = request.EventId, ProcessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted("/api/notifications", created);
    }

    public static NotificationEventRequest? ToRequest(IIntegrationEvent integrationEvent)
    {
        return integrationEvent switch
        {
            BookingCreated e => new(e.EventId, nameof(BookingCreated), [], new Dictionary<string, string> { ["bookingId"] = e.BookingId.ToString(), ["tableId"] = e.TableId.ToString(), ["startTime"] = e.StartTime.ToString("O") }),
            BookingConfirmed e => new(e.EventId, nameof(BookingConfirmed), [], new Dictionary<string, string> { ["bookingId"] = e.BookingId.ToString() }),
            BookingCancelled e => new(e.EventId, nameof(BookingCancelled), [], new Dictionary<string, string> { ["bookingId"] = e.BookingId.ToString(), ["reason"] = e.Reason }),
            PaymentSucceeded e => new(e.EventId, nameof(PaymentSucceeded), [], new Dictionary<string, string> { ["paymentId"] = e.PaymentId.ToString(), ["amount"] = e.Amount.ToString("0.##") }),
            PaymentFailed e => new(e.EventId, nameof(PaymentFailed), [], new Dictionary<string, string> { ["paymentId"] = e.PaymentId.ToString(), ["reason"] = e.Reason }),
            PaymentRefunded e => new(e.EventId, nameof(PaymentRefunded), [], new Dictionary<string, string> { ["paymentId"] = e.PaymentId.ToString(), ["amount"] = e.Amount.ToString("0.##"), ["totalRefunded"] = e.TotalRefunded.ToString("0.##") }),
            LowStockDetected e => new(e.EventId, nameof(LowStockDetected), [], new Dictionary<string, string> { ["branchId"] = e.BranchId.ToString(), ["tobaccoId"] = e.TobaccoId.ToString(), ["stockGrams"] = e.StockGrams.ToString("0.##") }),
            OrderServed e => new(e.EventId, nameof(OrderServed), [], new Dictionary<string, string> { ["orderId"] = e.OrderId.ToString(), ["branchId"] = e.BranchId.ToString() }),
            _ => null
        };
    }
}

sealed class NotificationRabbitMqConsumer(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<NotificationRabbitMqConsumer> logger) : BackgroundService
{
    private readonly bool _enabled = configuration.GetValue("RabbitMQ:Consumers:Enabled", false);
    private readonly string _queue = configuration["RabbitMQ:Consumers:NotificationQueue"] ?? "notification.events";
    private readonly string _exchange = configuration["RabbitMQ:Exchange"] ?? MessagingCatalog.Exchange;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) return;

        var factory = CreateFactory(configuration);
        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(_exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(_queue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = MessagingCatalog.DeadLetterExchange }, cancellationToken: stoppingToken);
        foreach (var routingKey in new[] { "booking.*", "payment.*", "inventory.low-stock-detected", "order.served" })
        {
            await channel.QueueBindAsync(_queue, _exchange, routingKey, cancellationToken: stoppingToken);
        }

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var eventName = args.BasicProperties.Type;
                if (string.IsNullOrWhiteSpace(eventName) && args.BasicProperties.Headers?.TryGetValue("event_name", out var header) == true) eventName = ReadHeaderAsString(header);
                var message = new OutboxMessage { EventName = eventName ?? string.Empty, Payload = Encoding.UTF8.GetString(args.Body.Span) };
                var integrationEvent = OutboxMessageSerializer.Deserialize(message);
                var request = integrationEvent is null ? null : NotificationEventProcessor.ToRequest(integrationEvent);
                if (integrationEvent is null) throw new InvalidOperationException($"Invalid notification event '{eventName}'.");
                if (request is null)
                {
                    logger.LogDebug("Skipping unsupported notification event '{EventName}'.", eventName);
                    await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: args.CancellationToken);
                    return;
                }
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var result = await NotificationEventProcessor.ApplyAsync(request, db, args.CancellationToken);
                if (result is IStatusCodeHttpResult { StatusCode: >= 400 }) throw new InvalidOperationException($"Notification event '{eventName}' was rejected.");
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: args.CancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ notification event handling failed.");
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
        ClientProvidedName = "notification-service"
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

public sealed record SendNotificationRequest(Guid UserId, string Channel, string Title, string Message, IReadOnlyDictionary<string, string> Metadata);
public sealed record UpdateNotificationPreferenceRequest(bool CrmEnabled, bool TelegramEnabled, bool SmsEnabled, bool EmailEnabled, bool PushEnabled);
public sealed record NotificationEventRequest(Guid EventId, string Event, IReadOnlyCollection<Guid> UserIds, IReadOnlyDictionary<string, string> Values);
