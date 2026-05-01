using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using HookahPlatform.NotificationService.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("notification-service");
builder.AddPostgresDbContext<NotificationDbContext>();

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
    var preference = await GetOrCreatePreferenceAsync(userId, db, cancellationToken);
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
    if (!await ChannelIsAllowedAsync(request.UserId, request.Channel, db, cancellationToken)) return HttpResults.Validation($"Channel '{request.Channel}' is disabled for this user.");
    var notification = new NotificationEntity { Id = Guid.NewGuid(), UserId = request.UserId, Channel = request.Channel, Title = request.Title, Message = request.Message, Metadata = JsonSerializer.Serialize(request.Metadata), CreatedAt = DateTimeOffset.UtcNow, ReadAt = null };
    db.Notifications.Add(notification);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/notifications/{notification.Id}", notification);
});

app.MapPost("/api/notifications/dispatch-event", async (NotificationEventRequest request, NotificationDbContext db, CancellationToken cancellationToken) =>
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
});

app.Run();

static async Task<NotificationPreferenceEntity> GetOrCreatePreferenceAsync(Guid userId, NotificationDbContext db, CancellationToken cancellationToken)
{
    var preference = await db.Preferences.FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
    if (preference is not null) return preference;
    preference = new NotificationPreferenceEntity { UserId = userId, CrmEnabled = true, TelegramEnabled = true, SmsEnabled = true, EmailEnabled = true, PushEnabled = true };
    db.Preferences.Add(preference);
    return preference;
}

static async Task<bool> ChannelIsAllowedAsync(Guid userId, string channel, NotificationDbContext db, CancellationToken cancellationToken)
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

static string Render(string template, IReadOnlyDictionary<string, string> values)
{
    var result = template;
    foreach (var (key, value) in values) result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
    return result;
}

public sealed record SendNotificationRequest(Guid UserId, string Channel, string Title, string Message, IReadOnlyDictionary<string, string> Metadata);
public sealed record UpdateNotificationPreferenceRequest(bool CrmEnabled, bool TelegramEnabled, bool SmsEnabled, bool EmailEnabled, bool PushEnabled);
public sealed record NotificationEventRequest(Guid EventId, string Event, IReadOnlyCollection<Guid> UserIds, IReadOnlyDictionary<string, string> Values);
