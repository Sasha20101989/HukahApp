using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("order-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var orders = new Dictionary<Guid, HookahOrder>();
var coalChanges = new List<CoalChange>();

app.MapGet("/api/orders", (Guid? branchId, string? status, DateOnly? date) =>
{
    var query = orders.Values.AsEnumerable();
    if (branchId is not null)
    {
        query = query.Where(order => order.BranchId == branchId);
    }
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(order => string.Equals(order.Status, status, StringComparison.OrdinalIgnoreCase));
    }
    if (date is not null)
    {
        query = query.Where(order => DateOnly.FromDateTime(order.CreatedAt.UtcDateTime) == date);
    }

    return Results.Ok(query.OrderByDescending(order => order.CreatedAt));
});

app.MapPost("/api/orders", async (CreateOrderRequest request, IEventPublisher events) =>
{
    if (request.MixId == Guid.Empty || request.BowlId == Guid.Empty || request.HookahId == Guid.Empty)
    {
        return HttpResults.Validation("Hookah, bowl and mix are required.");
    }

    var orderId = Guid.NewGuid();
    var item = new OrderItem(Guid.NewGuid(), orderId, request.HookahId, request.BowlId, request.MixId, request.Price ?? 850m, "NEW");
    var order = new HookahOrder(orderId, request.BranchId, request.TableId, request.ClientId, null, request.WaiterId, request.BookingId, "NEW", item.Price, request.Comment, DateTimeOffset.UtcNow, null, null, [item]);
    orders[order.Id] = order;

    await events.PublishAsync(new OrderCreated(order.Id, order.BranchId, order.TableId, order.ClientId, item.MixId, DateTimeOffset.UtcNow));
    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapGet("/api/orders/{id:guid}", (Guid id) =>
    orders.TryGetValue(id, out var order) ? Results.Ok(order) : HttpResults.NotFound("Order", id));

app.MapPatch("/api/orders/{id:guid}/status", async (Guid id, UpdateOrderStatusRequest request, IEventPublisher events) =>
{
    if (!orders.TryGetValue(id, out var order))
    {
        return HttpResults.NotFound("Order", id);
    }

    var normalized = request.Status.ToUpperInvariant();
    var servedAt = normalized == "SERVED" ? DateTimeOffset.UtcNow : order.ServedAt;
    var completedAt = normalized == "COMPLETED" ? DateTimeOffset.UtcNow : order.CompletedAt;
    var updated = order with { Status = normalized, ServedAt = servedAt, CompletedAt = completedAt };
    orders[id] = updated;

    await events.PublishAsync(new OrderStatusChanged(id, normalized, DateTimeOffset.UtcNow));
    if (normalized == "SERVED")
    {
        var item = updated.Items.First();
        await events.PublishAsync(new OrderServed(updated.Id, updated.BranchId, item.MixId, item.BowlId, servedAt!.Value, DateTimeOffset.UtcNow));
    }

    return Results.Ok(updated);
});

app.MapPatch("/api/orders/{id:guid}/assign-hookah-master", (Guid id, AssignHookahMasterRequest request) =>
{
    if (!orders.TryGetValue(id, out var order))
    {
        return HttpResults.NotFound("Order", id);
    }

    orders[id] = order with { HookahMasterId = request.HookahMasterId };
    return Results.Ok(orders[id]);
});

app.MapPost("/api/orders/{id:guid}/coal-change", (Guid id, CoalChangeRequest request) =>
{
    if (!orders.ContainsKey(id))
    {
        return HttpResults.NotFound("Order", id);
    }

    var change = new CoalChange(Guid.NewGuid(), id, request.ChangedAt ?? DateTimeOffset.UtcNow);
    coalChanges.Add(change);
    return Results.Created($"/api/orders/{id}/coal-change/{change.Id}", change);
});

app.MapDelete("/api/orders/{id:guid}", async (Guid id, CancelOrderRequest request, IEventPublisher events) =>
{
    if (!orders.TryGetValue(id, out var order))
    {
        return HttpResults.NotFound("Order", id);
    }

    orders[id] = order with { Status = "CANCELLED" };
    await events.PublishAsync(new OrderStatusChanged(id, "CANCELLED", DateTimeOffset.UtcNow));
    return Results.Ok(new { id, status = "CANCELLED", request.Reason });
});

app.Run();

public sealed record HookahOrder(Guid Id, Guid BranchId, Guid TableId, Guid? ClientId, Guid? HookahMasterId, Guid? WaiterId, Guid? BookingId, string Status, decimal TotalPrice, string? Comment, DateTimeOffset CreatedAt, DateTimeOffset? ServedAt, DateTimeOffset? CompletedAt, IReadOnlyCollection<OrderItem> Items);
public sealed record OrderItem(Guid Id, Guid OrderId, Guid HookahId, Guid BowlId, Guid MixId, decimal Price, string Status);
public sealed record CoalChange(Guid Id, Guid OrderId, DateTimeOffset ChangedAt);
public sealed record CreateOrderRequest(Guid BranchId, Guid TableId, Guid? ClientId, Guid HookahId, Guid BowlId, Guid MixId, Guid? BookingId, Guid? WaiterId, decimal? Price, string? Comment);
public sealed record UpdateOrderStatusRequest(string Status);
public sealed record AssignHookahMasterRequest(Guid HookahMasterId);
public sealed record CoalChangeRequest(DateTimeOffset? ChangedAt);
public sealed record CancelOrderRequest(string Reason);
