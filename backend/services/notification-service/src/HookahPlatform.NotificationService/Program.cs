using HookahPlatform.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("notification-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var notifications = new Dictionary<Guid, Notification>();

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
    var notification = new Notification(Guid.NewGuid(), request.UserId, request.Channel, request.Title, request.Message, DateTimeOffset.UtcNow, null);
    notifications[notification.Id] = notification;
    return Results.Created($"/api/notifications/{notification.Id}", notification);
});

app.Run();

public sealed record Notification(Guid Id, Guid UserId, string Channel, string Title, string Message, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record SendNotificationRequest(Guid UserId, string Channel, string Title, string Message);
