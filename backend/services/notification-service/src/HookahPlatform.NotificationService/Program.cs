using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.NotificationService.Persistence;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("notification-service");
builder.AddPostgresDbContext<NotificationDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<NotificationDbContext>("notification-service");

var notifications = new Dictionary<Guid, Notification>();
var preferences = new Dictionary<Guid, NotificationPreference>();
var templates = SeedTemplates();

app.MapGet("/api/notifications", (Guid? userId, bool? unreadOnly) =>
{
    var query = notifications.Values.AsEnumerable();
    if (userId is not null)
    {
        query = query.Where(notification => notification.UserId == userId);
    }
    if (unreadOnly == true)
    {
        query = query.Where(notification => notification.ReadAt is null);
    }

    return Results.Ok(query.OrderByDescending(notification => notification.CreatedAt));
});

app.MapGet("/api/notifications/templates", () => Results.Ok(templates.Values.OrderBy(template => template.Code)));

app.MapGet("/api/notifications/preferences/{userId:guid}", (Guid userId) =>
{
    if (!preferences.TryGetValue(userId, out var preference))
    {
        preference = new NotificationPreference(userId, true, true, true, true, true);
        preferences[userId] = preference;
    }

    return Results.Ok(preference);
});

app.MapPut("/api/notifications/preferences/{userId:guid}", (Guid userId, UpdateNotificationPreferenceRequest request) =>
{
    var preference = new NotificationPreference(
        userId,
        request.CrmEnabled,
        request.TelegramEnabled,
        request.SmsEnabled,
        request.EmailEnabled,
        request.PushEnabled);
    preferences[userId] = preference;
    return Results.Ok(preference);
});

app.MapPatch("/api/notifications/{id:guid}/read", (Guid id) =>
{
    if (!notifications.TryGetValue(id, out var notification))
    {
        return HttpResults.NotFound("Notification", id);
    }

    notifications[id] = notification with { ReadAt = DateTimeOffset.UtcNow };
    return Results.Ok(notifications[id]);
});

app.MapPost("/api/notifications/send", (SendNotificationRequest request) =>
{
    if (!ChannelIsAllowed(request.UserId, request.Channel, preferences))
    {
        return HttpResults.Validation($"Channel '{request.Channel}' is disabled for this user.");
    }

    var notification = new Notification(Guid.NewGuid(), request.UserId, request.Channel, request.Title, request.Message, request.Metadata, DateTimeOffset.UtcNow, null);
    notifications[notification.Id] = notification;
    return Results.Created($"/api/notifications/{notification.Id}", notification);
});

app.MapPost("/api/notifications/dispatch-event", (NotificationEventRequest request) =>
{
    var templateCode = request.Event switch
    {
        nameof(BookingCreated) => "booking.created.manager",
        nameof(BookingConfirmed) => "booking.confirmed.client",
        nameof(BookingCancelled) => "booking.cancelled.manager",
        nameof(PaymentSucceeded) => "payment.succeeded.client",
        nameof(PaymentRefunded) => "payment.refunded.client",
        nameof(LowStockDetected) => "inventory.low-stock.manager",
        nameof(OrderServed) => "order.served.coal-timer",
        _ => null
    };

    if (templateCode is null || !templates.TryGetValue(templateCode, out var template))
    {
        return HttpResults.Validation($"No notification template for event '{request.Event}'.");
    }

    IReadOnlyCollection<Guid> userIds = request.UserIds.Count == 0
        ? [Guid.Parse("90000000-0000-0000-0000-000000000001")]
        : request.UserIds;

    var created = new List<Notification>();
    foreach (var userId in userIds)
    {
        if (!ChannelIsAllowed(userId, template.Channel, preferences))
        {
            continue;
        }

        var notification = new Notification(
            Guid.NewGuid(),
            userId,
            template.Channel,
            Render(template.Title, request.Values),
            Render(template.Message, request.Values),
            request.Values,
            DateTimeOffset.UtcNow,
            null);

        notifications[notification.Id] = notification;
        created.Add(notification);
    }

    return Results.Accepted("/api/notifications", created);
});

app.Run();

static Dictionary<string, NotificationTemplate> SeedTemplates()
{
    return new(StringComparer.OrdinalIgnoreCase)
    {
        ["booking.created.manager"] = new("booking.created.manager", "CRM", "Новая бронь", "Стол {tableId} забронирован на {startTime}."),
        ["booking.confirmed.client"] = new("booking.confirmed.client", "PUSH", "Бронь подтверждена", "Ждем вас {startTime}."),
        ["booking.cancelled.manager"] = new("booking.cancelled.manager", "CRM", "Бронь отменена", "Бронь {bookingId} отменена."),
        ["payment.succeeded.client"] = new("payment.succeeded.client", "PUSH", "Оплата прошла", "Депозит {amount} ₽ получен."),
        ["payment.refunded.client"] = new("payment.refunded.client", "PUSH", "Возврат оформлен", "Возвращено {amount} ₽."),
        ["inventory.low-stock.manager"] = new("inventory.low-stock.manager", "CRM", "Низкий остаток", "Табак {tobaccoId}: осталось {stockGrams} г."),
        ["order.served.coal-timer"] = new("order.served.coal-timer", "CRM", "Кальян вынесен", "Запущен таймер углей по заказу {orderId}.")
    };
}

static bool ChannelIsAllowed(Guid userId, string channel, IDictionary<Guid, NotificationPreference> preferences)
{
    if (!preferences.TryGetValue(userId, out var preference))
    {
        return true;
    }

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

static string Render(string template, IReadOnlyDictionary<string, string> values)
{
    var result = template;
    foreach (var (key, value) in values)
    {
        result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
    }

    return result;
}

public sealed record Notification(Guid Id, Guid UserId, string Channel, string Title, string Message, IReadOnlyDictionary<string, string> Metadata, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record NotificationTemplate(string Code, string Channel, string Title, string Message);
public sealed record NotificationPreference(Guid UserId, bool CrmEnabled, bool TelegramEnabled, bool SmsEnabled, bool EmailEnabled, bool PushEnabled);
public sealed record SendNotificationRequest(Guid UserId, string Channel, string Title, string Message, IReadOnlyDictionary<string, string> Metadata);
public sealed record UpdateNotificationPreferenceRequest(bool CrmEnabled, bool TelegramEnabled, bool SmsEnabled, bool EmailEnabled, bool PushEnabled);
public sealed record NotificationEventRequest(string Event, IReadOnlyCollection<Guid> UserIds, IReadOnlyDictionary<string, string> Values);
