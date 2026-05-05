using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Tenancy;
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
builder.Services.AddHttpClient();
builder.Services.AddHostedService<NotificationRabbitMqConsumer>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<NotificationDbContext>("notification-service");

app.MapGet("/api/notifications", async (Guid? userId, bool? unreadOnly, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var query = db.Notifications.AsNoTracking().Where(notification => notification.TenantId == tenantId);
    if (userId is not null) query = query.Where(notification => notification.UserId == userId);
    if (unreadOnly == true) query = query.Where(notification => notification.ReadAt == null);
    return Results.Ok(await query.OrderByDescending(notification => notification.CreatedAt)
        .Select(notification => new
        {
            notification.Id,
            notification.UserId,
            notification.Channel,
            notification.Title,
            notification.Message,
            IsRead = notification.ReadAt != null,
            notification.CreatedAt
        })
        .ToListAsync(cancellationToken));
});

app.MapGet("/api/notifications/templates", async (NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    return Results.Ok(await db.Templates.AsNoTracking().Where(template => template.TenantId == tenantId).OrderBy(template => template.Code).ToListAsync(cancellationToken));
});

app.MapPost("/api/notifications/templates", async (UpsertNotificationTemplateRequest request, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Channel) || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
    {
        return HttpResults.Validation("Template code, channel, title and message are required.");
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var code = request.Code.Trim();
    var existing = await db.Templates.FirstOrDefaultAsync(template => template.TenantId == tenantId && template.Code == code, cancellationToken);
    if (existing is not null)
    {
        return HttpResults.Conflict($"Notification template '{code}' already exists.");
    }

    var template = new NotificationTemplateEntity
    {
        TenantId = tenantId,
        Code = code,
        Channel = request.Channel.Trim().ToUpperInvariant(),
        Title = request.Title.Trim(),
        Message = request.Message.Trim()
    };
    db.Templates.Add(template);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/notifications/templates/{template.Code}", template);
});

app.MapPut("/api/notifications/templates/{code}", async (string code, UpsertNotificationTemplateRequest request, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var template = await db.Templates.FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Code == code, cancellationToken);
    if (template is null) return Results.NotFound(new ProblemDetailsDto("not_found", $"NotificationTemplate '{code}' was not found."));
    if (string.IsNullOrWhiteSpace(request.Channel) || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
    {
        return HttpResults.Validation("Template channel, title and message are required.");
    }

    template.Channel = request.Channel.Trim().ToUpperInvariant();
    template.Title = request.Title.Trim();
    template.Message = request.Message.Trim();
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(template);
});

app.MapDelete("/api/notifications/templates/{code}", async (string code, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var template = await db.Templates.FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Code == code, cancellationToken);
    if (template is null) return Results.NotFound(new ProblemDetailsDto("not_found", $"NotificationTemplate '{code}' was not found."));
    db.Templates.Remove(template);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/notifications/preferences/{userId:guid}", async (Guid userId, HttpContext context, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (!CanActForUser(context, userId)) return Results.Json(new ProblemDetailsDto("forbidden", "Notification preferences can only be managed for the current user."), statusCode: StatusCodes.Status403Forbidden);
    var preference = await NotificationEventProcessor.GetOrCreatePreferenceAsync(tenantContext.GetTenantIdOrDemo(), userId, db, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(preference);
});

app.MapPut("/api/notifications/preferences/{userId:guid}", async (Guid userId, UpdateNotificationPreferenceRequest request, HttpContext context, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (!CanActForUser(context, userId)) return Results.Json(new ProblemDetailsDto("forbidden", "Notification preferences can only be managed for the current user."), statusCode: StatusCodes.Status403Forbidden);
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var preference = await db.Preferences.FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.UserId == userId, cancellationToken);
    if (preference is null)
    {
        preference = new NotificationPreferenceEntity { TenantId = tenantId, UserId = userId };
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

app.MapPatch("/api/notifications/{id:guid}/read", async (Guid id, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var notification = await db.Notifications.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (notification is null) return HttpResults.NotFound("Notification", id);
    notification.ReadAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(notification);
});

app.MapDelete("/api/notifications/{id:guid}", async (Guid id, HttpContext context, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var notification = await db.Notifications.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (notification is null) return HttpResults.NotFound("Notification", id);
    if (!CanActForUser(context, notification.UserId)) return Results.Json(new ProblemDetailsDto("forbidden", "Notifications can only be deleted by the recipient or booking managers."), statusCode: StatusCodes.Status403Forbidden);
    db.Notifications.Remove(notification);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/notifications/channels", async (NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var channels = await db.TenantChannels.AsNoTracking()
        .Where(channel => channel.TenantId == tenantId)
        .OrderBy(channel => channel.Channel)
        .Select(channel => new TenantNotificationChannelDto(channel.Id, channel.Channel, channel.IsActive, channel.CreatedAt, channel.UpdatedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(channels);
});

app.MapPut("/api/notifications/channels/{channel}", async (string channel, UpsertTenantNotificationChannelRequest request, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var channelCode = channel.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(channelCode)) return HttpResults.Validation("Channel is required.");
    if (string.IsNullOrWhiteSpace(request.EncryptedSettings)) return HttpResults.Validation("Encrypted settings are required.");
    var now = DateTimeOffset.UtcNow;
    var entity = await db.TenantChannels.FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Channel == channelCode, cancellationToken);
    if (entity is null)
    {
        entity = new TenantNotificationChannelEntity { Id = Guid.NewGuid(), TenantId = tenantId, Channel = channelCode, CreatedAt = now };
        db.TenantChannels.Add(entity);
    }
    entity.EncryptedSettings = request.EncryptedSettings.Trim();
    entity.IsActive = request.IsActive;
    entity.UpdatedAt = now;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new TenantNotificationChannelDto(entity.Id, entity.Channel, entity.IsActive, entity.CreatedAt, entity.UpdatedAt));
});

app.MapPost("/api/notifications/send", async (SendNotificationRequest request, NotificationDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (request.UserId == Guid.Empty) return HttpResults.Validation("User is required.");
    if (string.IsNullOrWhiteSpace(request.Channel) || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
    {
        return HttpResults.Validation("Notification channel, title and message are required.");
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var channel = request.Channel.Trim().ToUpperInvariant();
    var title = request.Title.Trim();
    var message = request.Message.Trim();
    if (!await NotificationEventProcessor.TenantChannelIsActiveAsync(tenantId, channel, db, cancellationToken)) return HttpResults.Validation($"Channel '{channel}' is not active for this tenant.");
    if (!await NotificationEventProcessor.ChannelIsAllowedAsync(tenantId, request.UserId, channel, db, cancellationToken)) return HttpResults.Validation($"Channel '{channel}' is disabled for this user.");
    var notification = new NotificationEntity
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = request.UserId,
        Channel = channel,
        Title = title,
        Message = message,
        Metadata = JsonDocument.Parse(JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, string>())),
        CreatedAt = DateTimeOffset.UtcNow,
        ReadAt = null
    };
    db.Notifications.Add(notification);
    db.Deliveries.Add(NotificationEventProcessor.CreateDelivery(tenantId, notification.Id, channel, request.UserId.ToString(), "STORED", null));
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/notifications/{notification.Id}", notification);
});

app.MapPost("/api/notifications/dispatch-event", async (NotificationEventRequest request, NotificationDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var result = await NotificationEventProcessor.ApplyAsync(tenantContext.GetTenantIdOrDemo(), request, db, httpClientFactory, configuration, cancellationToken);
    return result;
});

app.Run();

static bool CanActForUser(HttpContext context, Guid userId)
{
    return HasForwardedPermission(context, PermissionCodes.BookingsManage) ||
           Guid.TryParse(context.Request.Headers[ServiceAccessControl.UserIdHeader].ToString(), out var forwardedUserId) && forwardedUserId == userId;
}

static bool HasForwardedPermission(HttpContext context, string permission)
{
    var permissions = context.Request.Headers[ServiceAccessControl.UserPermissionsHeader].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return permissions.Contains("*", StringComparer.OrdinalIgnoreCase) ||
           permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

static class NotificationEventProcessor
{
    public static async Task<NotificationPreferenceEntity> GetOrCreatePreferenceAsync(Guid tenantId, Guid userId, NotificationDbContext db, CancellationToken cancellationToken)
    {
        var preference = await db.Preferences.FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.UserId == userId, cancellationToken);
        if (preference is not null) return preference;
        preference = new NotificationPreferenceEntity { TenantId = tenantId, UserId = userId, CrmEnabled = true, TelegramEnabled = true, SmsEnabled = true, EmailEnabled = true, PushEnabled = true };
        db.Preferences.Add(preference);
        return preference;
    }

    public static async Task<bool> ChannelIsAllowedAsync(Guid tenantId, Guid userId, string channel, NotificationDbContext db, CancellationToken cancellationToken)
    {
        var preference = await GetOrCreatePreferenceAsync(tenantId, userId, db, cancellationToken);
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

    public static async Task<bool> TenantChannelIsActiveAsync(Guid tenantId, string channel, NotificationDbContext db, CancellationToken cancellationToken)
    {
        if (channel.Equals("CRM", StringComparison.OrdinalIgnoreCase)) return true;
        return await db.TenantChannels.AsNoTracking().AnyAsync(candidate => candidate.TenantId == tenantId && candidate.Channel == channel.ToUpperInvariant() && candidate.IsActive, cancellationToken);
    }

    public static NotificationDeliveryEntity CreateDelivery(Guid tenantId, Guid? notificationId, string channel, string recipient, string status, string? error)
    {
        return new NotificationDeliveryEntity { Id = Guid.NewGuid(), TenantId = tenantId, NotificationId = notificationId, Channel = channel, Recipient = recipient, Status = status, Error = error, CreatedAt = DateTimeOffset.UtcNow };
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var result = template;
        foreach (var (key, value) in values) result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    public static async Task<IResult> ApplyAsync(Guid tenantId, NotificationEventRequest request, NotificationDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
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
        var template = await db.Templates.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Code == templateCode, cancellationToken);
        if (template is null) return HttpResults.Validation($"No notification template for event '{request.Event}'.");

        var userIds = await ResolveRecipientsAsync(request, httpClientFactory, configuration, cancellationToken);
        if (userIds.Count == 0)
        {
            return HttpResults.Validation($"No notification recipients for event '{request.Event}'. Provide userIds or include a resolvable branchId/clientId.");
        }

        var created = new List<NotificationEntity>();
        foreach (var userId in userIds)
        {
            if (!await TenantChannelIsActiveAsync(tenantId, template.Channel, db, cancellationToken))
            {
                db.Deliveries.Add(CreateDelivery(tenantId, null, template.Channel, userId.ToString(), "SKIPPED", "tenant channel inactive"));
                continue;
            }
            if (!await ChannelIsAllowedAsync(tenantId, userId, template.Channel, db, cancellationToken))
            {
                db.Deliveries.Add(CreateDelivery(tenantId, null, template.Channel, userId.ToString(), "SKIPPED", "user channel disabled"));
                continue;
            }
            var notification = new NotificationEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Channel = template.Channel,
                Title = Render(template.Title, request.Values),
                Message = Render(template.Message, request.Values),
                Metadata = JsonDocument.Parse(JsonSerializer.Serialize(request.Values)),
                CreatedAt = DateTimeOffset.UtcNow,
                ReadAt = null
            };
            db.Notifications.Add(notification);
            db.Deliveries.Add(CreateDelivery(tenantId, notification.Id, template.Channel, userId.ToString(), "STORED", null));
            created.Add(notification);
        }
        db.ProcessedEvents.Add(new ProcessedIntegrationEventEntity { Handler = "notification-service", EventId = request.EventId, ProcessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted("/api/notifications", created);
    }

    private static async Task<IReadOnlyCollection<Guid>> ResolveRecipientsAsync(NotificationEventRequest request, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (request.UserIds.Count > 0) return request.UserIds.Distinct().ToArray();

        if ((request.Event is nameof(BookingConfirmed) or nameof(PaymentSucceeded) or nameof(PaymentRefunded)) &&
            TryReadGuid(request.Values, "clientId", out var clientId))
        {
            return [clientId];
        }

        if (TryReadGuid(request.Values, "branchId", out var branchId))
        {
            return await ResolveBranchRecipientsAsync(branchId, request.Event, httpClientFactory, configuration, cancellationToken);
        }

        if ((request.Event is nameof(BookingConfirmed) or nameof(BookingCancelled)) &&
            TryReadGuid(request.Values, "bookingId", out var bookingId))
        {
            var booking = await GetBookingAsync(bookingId, httpClientFactory, configuration, cancellationToken);
            if (booking is null) return [];

            return request.Event switch
            {
                nameof(BookingConfirmed) => [booking.ClientId],
                nameof(BookingCancelled) => await ResolveBranchRecipientsAsync(booking.BranchId, request.Event, httpClientFactory, configuration, cancellationToken),
                _ => []
            };
        }

        if (TryReadGuid(request.Values, "paymentId", out var paymentId))
        {
            var payment = await GetPaymentAsync(paymentId, httpClientFactory, configuration, cancellationToken);
            if (payment is null) return [];

            if (request.Event is nameof(PaymentSucceeded) or nameof(PaymentRefunded)) return [payment.ClientId];

            if (request.Event == nameof(PaymentFailed))
            {
                var relatedBranchId = await ResolvePaymentBranchIdAsync(payment, httpClientFactory, configuration, cancellationToken);
                if (relatedBranchId is not null)
                {
                    return await ResolveBranchRecipientsAsync(relatedBranchId.Value, request.Event, httpClientFactory, configuration, cancellationToken);
                }
            }
        }

        return [];
    }

    private static async Task<IReadOnlyCollection<Guid>> ResolveBranchRecipientsAsync(Guid branchId, string eventName, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var roles = eventName switch
        {
            nameof(OrderServed) => new[] { RoleCodes.HookahMaster, RoleCodes.Manager },
            _ => new[] { RoleCodes.Manager }
        };

        var recipients = new List<Guid>();
        foreach (var role in roles)
        {
            recipients.AddRange(await GetBranchUsersAsync(branchId, role, httpClientFactory, configuration, cancellationToken));
        }

        return recipients.Distinct().ToArray();
    }

    private static async Task<Guid?> ResolvePaymentBranchIdAsync(PaymentLookupDto payment, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (payment.BookingId is not null)
        {
            return (await GetBookingAsync(payment.BookingId.Value, httpClientFactory, configuration, cancellationToken))?.BranchId;
        }

        if (payment.OrderId is not null)
        {
            return (await GetOrderAsync(payment.OrderId.Value, httpClientFactory, configuration, cancellationToken))?.BranchId;
        }

        return null;
    }

    private static async Task<IReadOnlyCollection<Guid>> GetBranchUsersAsync(Guid branchId, string role, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var userBaseUrl = configuration["Services:user-service:BaseUrl"] ?? "http://user-service:8080";
        var client = httpClientFactory.CreateClient("notification-service");
        var users = await client.GetFromJsonAsync<UserProfileDto[]>($"{userBaseUrl}/api/users?branchId={branchId}&role={Uri.EscapeDataString(role)}&status=active", cancellationToken) ?? [];
        return users.Select(user => user.Id).ToArray();
    }

    private static async Task<BookingLookupDto?> GetBookingAsync(Guid bookingId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var bookingBaseUrl = configuration["Services:booking-service:BaseUrl"] ?? "http://booking-service:8080";
        var client = httpClientFactory.CreateClient("notification-service");
        return await client.GetFromJsonAsync<BookingLookupDto>($"{bookingBaseUrl}/api/bookings/{bookingId}", cancellationToken);
    }

    private static async Task<PaymentLookupDto?> GetPaymentAsync(Guid paymentId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var paymentBaseUrl = configuration["Services:payment-service:BaseUrl"] ?? "http://payment-service:8080";
        var client = httpClientFactory.CreateClient("notification-service");
        return await client.GetFromJsonAsync<PaymentLookupDto>($"{paymentBaseUrl}/api/payments/{paymentId}", cancellationToken);
    }

    private static async Task<OrderLookupDto?> GetOrderAsync(Guid orderId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var orderBaseUrl = configuration["Services:order-service:BaseUrl"] ?? "http://order-service:8080";
        var client = httpClientFactory.CreateClient("notification-service");
        return await client.GetFromJsonAsync<OrderLookupDto>($"{orderBaseUrl}/api/orders/{orderId}", cancellationToken);
    }

    private static bool TryReadGuid(IReadOnlyDictionary<string, string> values, string key, out Guid id)
    {
        id = Guid.Empty;
        return values.TryGetValue(key, out var value) && Guid.TryParse(value, out id);
    }

    public static NotificationEventRequest? ToRequest(IIntegrationEvent integrationEvent)
    {
        return integrationEvent switch
        {
            BookingCreated e => new(e.EventId, nameof(BookingCreated), [], new Dictionary<string, string> { ["bookingId"] = e.BookingId.ToString(), ["branchId"] = e.BranchId.ToString(), ["tableId"] = e.TableId.ToString(), ["clientId"] = e.ClientId.ToString(), ["startTime"] = e.StartTime.ToString("O") }),
            BookingConfirmed e => new(e.EventId, nameof(BookingConfirmed), [], new Dictionary<string, string> { ["bookingId"] = e.BookingId.ToString() }),
            BookingCancelled e => new(e.EventId, nameof(BookingCancelled), [], new Dictionary<string, string> { ["bookingId"] = e.BookingId.ToString(), ["reason"] = e.Reason }),
            PaymentSucceeded e => new(e.EventId, nameof(PaymentSucceeded), [], AddOptionalIds(new Dictionary<string, string> { ["paymentId"] = e.PaymentId.ToString(), ["amount"] = e.Amount.ToString("0.##") }, e.BookingId, e.OrderId)),
            PaymentFailed e => new(e.EventId, nameof(PaymentFailed), [], new Dictionary<string, string> { ["paymentId"] = e.PaymentId.ToString(), ["reason"] = e.Reason }),
            PaymentRefunded e => new(e.EventId, nameof(PaymentRefunded), [], AddOptionalIds(new Dictionary<string, string> { ["paymentId"] = e.PaymentId.ToString(), ["amount"] = e.Amount.ToString("0.##"), ["totalRefunded"] = e.TotalRefunded.ToString("0.##") }, e.BookingId, e.OrderId)),
            LowStockDetected e => new(e.EventId, nameof(LowStockDetected), [], new Dictionary<string, string> { ["branchId"] = e.BranchId.ToString(), ["tobaccoId"] = e.TobaccoId.ToString(), ["stockGrams"] = e.StockGrams.ToString("0.##") }),
            OrderServed e => new(e.EventId, nameof(OrderServed), [], new Dictionary<string, string> { ["orderId"] = e.OrderId.ToString(), ["branchId"] = e.BranchId.ToString() }),
            _ => null
        };
    }

    private static Dictionary<string, string> AddOptionalIds(Dictionary<string, string> values, Guid? bookingId, Guid? orderId)
    {
        if (bookingId is not null) values["bookingId"] = bookingId.Value.ToString();
        if (orderId is not null) values["orderId"] = orderId.Value.ToString();
        return values;
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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
                        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                        var result = await NotificationEventProcessor.ApplyAsync(TenantConstants.DemoTenantId, request, db, httpClientFactory, configuration, args.CancellationToken);
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ connection failed (host={Host}). Retrying...", configuration["RabbitMQ:HostName"] ?? "localhost");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
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

public sealed record SendNotificationRequest(Guid UserId, string Channel, string Title, string Message, IReadOnlyDictionary<string, string>? Metadata);
public sealed record UpsertNotificationTemplateRequest(string Code, string Channel, string Title, string Message);
public sealed record TenantNotificationChannelDto(Guid Id, string Channel, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record UpsertTenantNotificationChannelRequest(string EncryptedSettings, bool IsActive);
public sealed record UpdateNotificationPreferenceRequest(bool CrmEnabled, bool TelegramEnabled, bool SmsEnabled, bool EmailEnabled, bool PushEnabled);
public sealed record NotificationEventRequest(Guid EventId, string Event, IReadOnlyCollection<Guid> UserIds, IReadOnlyDictionary<string, string> Values);
public sealed record UserProfileDto(Guid Id, string Name, string Phone, string? Email, string Role, Guid? BranchId, string Status);
public sealed record BookingLookupDto(Guid Id, Guid ClientId, Guid BranchId, Guid TableId, Guid? HookahId, Guid? BowlId, Guid? MixId, DateTimeOffset StartTime, DateTimeOffset EndTime, int GuestsCount, string Status, decimal DepositAmount);
public sealed record PaymentLookupDto(Guid Id, Guid ClientId, Guid? OrderId, Guid? BookingId, decimal OriginalAmount, decimal DiscountAmount, decimal PayableAmount, decimal RefundedAmount, string Currency, string Provider, string? Promocode, string? ExternalPaymentId, string Status, string Type, DateTimeOffset CreatedAt);
public sealed record OrderLookupDto(Guid Id, Guid BranchId, Guid TableId, Guid? ClientId, Guid? HookahMasterId, Guid? WaiterId, Guid? BookingId, string Status, decimal TotalPrice, string? Comment, DateTimeOffset CreatedAt);
