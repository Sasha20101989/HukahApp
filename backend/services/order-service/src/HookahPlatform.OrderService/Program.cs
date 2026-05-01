using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using HookahPlatform.OrderService.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("order-service");
builder.AddPostgresDbContext<OrderDbContext>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<OrderDbContext>("order-service");

var allowedOrderTransitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    [OrderStatuses.New] = [OrderStatuses.Accepted, OrderStatuses.Preparing, OrderStatuses.Cancelled],
    [OrderStatuses.Accepted] = [OrderStatuses.Preparing, OrderStatuses.Cancelled],
    [OrderStatuses.Preparing] = [OrderStatuses.Ready, OrderStatuses.Served, OrderStatuses.Cancelled],
    [OrderStatuses.Ready] = [OrderStatuses.Served, OrderStatuses.Cancelled],
    [OrderStatuses.Served] = [OrderStatuses.Smoking, OrderStatuses.Completed, OrderStatuses.Cancelled],
    [OrderStatuses.Smoking] = [OrderStatuses.CoalChangeRequired, OrderStatuses.Completed, OrderStatuses.Cancelled],
    [OrderStatuses.CoalChangeRequired] = [OrderStatuses.Smoking, OrderStatuses.Completed, OrderStatuses.Cancelled],
    [OrderStatuses.Completed] = [],
    [OrderStatuses.Cancelled] = []
};

app.MapGet("/api/orders", async (Guid? branchId, string? status, DateOnly? date, OrderDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Orders.AsNoTracking();
    if (branchId is not null) query = query.Where(order => order.BranchId == branchId);
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!allowedOrderTransitions.ContainsKey(status)) return HttpResults.Validation($"Unsupported order status '{status}'.");
        query = query.Where(order => order.Status == status);
    }
    if (date is not null) query = query.Where(order => DateOnly.FromDateTime(order.CreatedAt.UtcDateTime) == date);
    var orders = await query.OrderByDescending(order => order.CreatedAt).ToListAsync(cancellationToken);
    return Results.Ok(await ToOrderDtosAsync(orders, db, cancellationToken));
});

app.MapGet("/api/orders/status-flow", () => Results.Ok(allowedOrderTransitions.Select(rule => new { status = rule.Key, next = rule.Value })));

app.MapGet("/api/orders/runtime/branch/{branchId:guid}", async (Guid branchId, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var ids = await cache.GetJsonAsync<Guid[]>(ActiveOrdersIndexKey(branchId), cancellationToken) ?? [];
    var orders = new List<ActiveOrderRuntimeState>();
    foreach (var id in ids)
    {
        var state = await cache.GetJsonAsync<ActiveOrderRuntimeState>(ActiveOrderKey(id), cancellationToken);
        if (state is not null && state.BranchId == branchId) orders.Add(state);
    }
    return Results.Ok(orders.OrderByDescending(order => order.UpdatedAt));
});

app.MapPost("/api/orders", async (CreateOrderRequest request, OrderDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (request.MixId == Guid.Empty || request.BowlId == Guid.Empty || request.HookahId == Guid.Empty) return HttpResults.Validation("Hookah, bowl and mix are required.");

    var resourceCheck = await CheckResourcesAvailableAsync(request.TableId, request.HookahId, httpClientFactory, configuration, cancellationToken);
    if (!resourceCheck.IsAvailable) return Results.Conflict(new { code = "resource_unavailable", message = resourceCheck.Message });

    var mix = await GetMixAsync(request.MixId, httpClientFactory, configuration, cancellationToken);
    if (mix is null) return HttpResults.Validation("Mix must exist before creating an order.");
    if (mix.BowlId != request.BowlId) return HttpResults.Validation("Order bowl must match the selected mix bowl.");

    var availability = await CheckMixAvailabilityAsync(request.BranchId, mix, httpClientFactory, configuration, cancellationToken);
    if (!availability.IsAvailable)
    {
        return Results.Conflict(new { code = "mix_unavailable", message = "Cannot create order because the mix is not available in branch inventory.", unavailableItems = availability.Items.Where(item => !item.IsAvailable) });
    }

    var orderId = Guid.NewGuid();
    var price = request.Price ?? 850m;
    var order = new OrderEntity { Id = orderId, BranchId = request.BranchId, TableId = request.TableId, ClientId = request.ClientId, HookahMasterId = null, WaiterId = request.WaiterId, BookingId = request.BookingId, Status = OrderStatuses.New, TotalPrice = price, Comment = request.Comment, CreatedAt = DateTimeOffset.UtcNow, ServedAt = null, CompletedAt = null, InventoryWrittenOffAt = null, PaymentId = null, PaidAmount = 0, PaidAt = null };
    var item = new OrderItemEntity { Id = Guid.NewGuid(), OrderId = orderId, HookahId = request.HookahId, BowlId = request.BowlId, MixId = request.MixId, Price = price, Status = OrderStatuses.New };
    db.Orders.Add(order);
    db.OrderItems.Add(item);
    var created = new OrderCreated(order.Id, order.BranchId, order.TableId, order.ClientId, item.MixId, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(created);
    await db.SaveChangesAsync(cancellationToken);

    await MarkResourcesInUseAsync(request.TableId, request.HookahId, httpClientFactory, configuration, cancellationToken);
    await StoreActiveOrderStateAsync(cache, ToOrderDto(order, [item]), item.HookahId, null, cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, created, outboxMessage, cancellationToken);
    return Results.Created($"/api/orders/{order.Id}", ToOrderDto(order, [item]));
});

app.MapGet("/api/orders/{id:guid}", async (Guid id, OrderDbContext db, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (order is null) return HttpResults.NotFound("Order", id);
    var items = await db.OrderItems.AsNoTracking().Where(item => item.OrderId == id).ToListAsync(cancellationToken);
    return Results.Ok(ToOrderDto(order, items));
});

app.MapPatch("/api/orders/{id:guid}/status", async (Guid id, UpdateOrderStatusRequest request, OrderDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (order is null) return HttpResults.NotFound("Order", id);
    var items = await db.OrderItems.Where(item => item.OrderId == id).ToListAsync(cancellationToken);
    var normalized = request.Status.ToUpperInvariant();
    if (!allowedOrderTransitions.ContainsKey(normalized)) return HttpResults.Validation($"Unsupported order status '{request.Status}'.");
    if (!CanTransition(order.Status, normalized, allowedOrderTransitions)) return HttpResults.Conflict($"Order status cannot transition from {order.Status} to {normalized}.");

    var now = DateTimeOffset.UtcNow;
    var outboxEvents = new List<IIntegrationEvent>();
    var shouldWriteOff = normalized == OrderStatuses.Served && order.InventoryWrittenOffAt is null;
    if (shouldWriteOff)
    {
        var item = items.First();
        order.InventoryWrittenOffAt = now;
        order.ServedAt ??= now;
        outboxEvents.Add(new OrderServed(order.Id, order.BranchId, item.MixId, item.BowlId, order.ServedAt.Value, now));
    }

    order.Status = normalized;
    if (normalized == OrderStatuses.Served) order.ServedAt ??= now;
    if (normalized == OrderStatuses.Completed) order.CompletedAt ??= now;
    outboxEvents.Add(new OrderStatusChanged(id, normalized, now));
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    if (normalized == OrderStatuses.Served) await SetCoalTimerAsync(cache, order.Id, order.ServedAt!.Value, cancellationToken);
    if (normalized is OrderStatuses.Completed or OrderStatuses.Cancelled) await cache.RemoveAsync(CoalTimerKey(order.Id), cancellationToken);
    var runtimeOrder = ToOrderDto(order, items);
    var runtimeHookahId = items.FirstOrDefault()?.HookahId;
    var runtimeCoalTimer = await cache.GetJsonAsync<CoalTimerState>(CoalTimerKey(order.Id), cancellationToken);
    if (normalized is OrderStatuses.Completed or OrderStatuses.Cancelled)
    {
        await RemoveActiveOrderStateAsync(cache, order.BranchId, order.Id, cancellationToken);
    }
    else if (runtimeHookahId is not null)
    {
        await StoreActiveOrderStateAsync(cache, runtimeOrder, runtimeHookahId.Value, runtimeCoalTimer?.NextCoalChangeAt, cancellationToken);
    }
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    if (normalized == OrderStatuses.Completed)
    {
        var item = items.First();
        await ReleaseResourcesAsync(order.TableId, item.HookahId, httpClientFactory, configuration, cancellationToken);
    }

    return Results.Ok(ToOrderDto(order, items));
});

app.MapPatch("/api/orders/{id:guid}/assign-hookah-master", async (Guid id, AssignHookahMasterRequest request, OrderDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (order is null) return HttpResults.NotFound("Order", id);
    if (order.Status is OrderStatuses.Completed or OrderStatuses.Cancelled) return HttpResults.Conflict("Cannot assign hookah master to completed or cancelled order.");
    order.HookahMasterId = request.HookahMasterId;
    await db.SaveChangesAsync(cancellationToken);
    var items = await db.OrderItems.AsNoTracking().Where(item => item.OrderId == id).ToListAsync(cancellationToken);
    if (items.FirstOrDefault() is { } item) await StoreActiveOrderStateAsync(cache, ToOrderDto(order, items), item.HookahId, null, cancellationToken);
    return Results.Ok(ToOrderDto(order, items));
});

app.MapPatch("/api/orders/{id:guid}/payment-succeeded", async (Guid id, OrderPaymentSucceededRequest request, OrderDbContext db, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (order is null) return HttpResults.NotFound("Order", id);
    order.PaymentId = request.PaymentId;
    order.PaidAmount = request.Amount;
    order.PaidAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    var items = await db.OrderItems.AsNoTracking().Where(item => item.OrderId == id).ToListAsync(cancellationToken);
    return Results.Ok(ToOrderDto(order, items));
});

app.MapPost("/api/orders/{id:guid}/coal-change", async (Guid id, CoalChangeRequest request, OrderDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (order is null) return HttpResults.NotFound("Order", id);
    if (order.Status is not OrderStatuses.Served and not OrderStatuses.Smoking and not OrderStatuses.CoalChangeRequired) return HttpResults.Conflict("Coal change can be registered only after the order is served.");
    var change = new CoalChangeEntity { Id = Guid.NewGuid(), OrderId = id, ChangedAt = request.ChangedAt ?? DateTimeOffset.UtcNow };
    db.CoalChanges.Add(change);
    order.Status = OrderStatuses.Smoking;
    await db.SaveChangesAsync(cancellationToken);
    await SetCoalTimerAsync(cache, id, change.ChangedAt, cancellationToken);
    var items = await db.OrderItems.AsNoTracking().Where(item => item.OrderId == id).ToListAsync(cancellationToken);
    if (items.FirstOrDefault() is { } item) await StoreActiveOrderStateAsync(cache, ToOrderDto(order, items), item.HookahId, change.ChangedAt.AddMinutes(20), cancellationToken);
    return Results.Created($"/api/orders/{id}/coal-change/{change.Id}", change);
});

app.MapGet("/api/orders/{id:guid}/coal-timer", async (Guid id, OrderDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (order is null) return HttpResults.NotFound("Order", id);
    var changes = await db.CoalChanges.AsNoTracking().Where(change => change.OrderId == id).OrderByDescending(change => change.ChangedAt).ToListAsync(cancellationToken);
    var cached = await cache.GetJsonAsync<CoalTimerState>(CoalTimerKey(id), cancellationToken);
    if (cached is not null) return Results.Ok(new { order.Id, order.Status, cached.LastChangedAt, cached.NextCoalChangeAt, changes });
    var lastChange = changes.FirstOrDefault();
    var lastChangedAt = lastChange?.ChangedAt ?? order.ServedAt;
    var nextCoalChangeAt = lastChangedAt?.AddMinutes(20);
    if (lastChangedAt is not null && order.Status is OrderStatuses.Served or OrderStatuses.Smoking or OrderStatuses.CoalChangeRequired)
    {
        await cache.SetJsonAsync(CoalTimerKey(id), new CoalTimerState(id, lastChangedAt.Value, nextCoalChangeAt!.Value), TimeSpan.FromHours(4), cancellationToken);
    }
    return Results.Ok(new { order.Id, order.Status, LastChangedAt = lastChangedAt, NextCoalChangeAt = nextCoalChangeAt, changes });
});

app.MapDelete("/api/orders/{id:guid}", async (Guid id, CancelOrderRequest request, OrderDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (order is null) return HttpResults.NotFound("Order", id);
    if (!CanTransition(order.Status, OrderStatuses.Cancelled, allowedOrderTransitions)) return HttpResults.Conflict($"Order status cannot transition from {order.Status} to {OrderStatuses.Cancelled}.");
    var items = await db.OrderItems.AsNoTracking().Where(item => item.OrderId == id).ToListAsync(cancellationToken);
    order.Status = OrderStatuses.Cancelled;
    var cancelled = new OrderStatusChanged(id, OrderStatuses.Cancelled, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(cancelled);
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(CoalTimerKey(id), cancellationToken);
    await RemoveActiveOrderStateAsync(cache, order.BranchId, order.Id, cancellationToken);
    var item = items.First();
    await ReleaseResourcesAsync(order.TableId, item.HookahId, httpClientFactory, configuration, cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, cancelled, outboxMessage, cancellationToken);
    return Results.Ok(new { id, status = OrderStatuses.Cancelled, request.Reason });
});

app.Run();

static async Task<IReadOnlyCollection<HookahOrder>> ToOrderDtosAsync(IReadOnlyCollection<OrderEntity> orders, OrderDbContext db, CancellationToken cancellationToken)
{
    var orderIds = orders.Select(order => order.Id).ToArray();
    var itemGroups = await db.OrderItems.AsNoTracking().Where(item => orderIds.Contains(item.OrderId)).GroupBy(item => item.OrderId).ToDictionaryAsync(group => group.Key, group => group.ToList(), cancellationToken);
    return orders.Select(order => ToOrderDto(order, itemGroups.TryGetValue(order.Id, out var items) ? items : [])).ToArray();
}

static HookahOrder ToOrderDto(OrderEntity order, IReadOnlyCollection<OrderItemEntity> items)
{
    return new HookahOrder(order.Id, order.BranchId, order.TableId, order.ClientId, order.HookahMasterId, order.WaiterId, order.BookingId, order.Status, order.TotalPrice, order.Comment, order.CreatedAt, order.ServedAt, order.CompletedAt, null, order.InventoryWrittenOffAt, order.PaymentId, order.PaidAmount, order.PaidAt, items.Select(item => new OrderItem(item.Id, item.OrderId, item.HookahId, item.BowlId, item.MixId, item.Price, item.Status)).ToArray());
}

static bool CanTransition(string currentStatus, string nextStatus, IReadOnlyDictionary<string, string[]> allowedOrderTransitions)
{
    if (currentStatus.Equals(nextStatus, StringComparison.OrdinalIgnoreCase)) return true;
    return allowedOrderTransitions.TryGetValue(currentStatus, out var allowedNextStatuses) && allowedNextStatuses.Contains(nextStatus, StringComparer.OrdinalIgnoreCase);
}

static string CoalTimerKey(Guid orderId) => $"order:{orderId}:coal-timer";

static Task SetCoalTimerAsync(IDistributedCache cache, Guid orderId, DateTimeOffset lastChangedAt, CancellationToken cancellationToken)
{
    return cache.SetJsonAsync(CoalTimerKey(orderId), new CoalTimerState(orderId, lastChangedAt, lastChangedAt.AddMinutes(20)), TimeSpan.FromHours(4), cancellationToken);
}

static string ActiveOrdersIndexKey(Guid branchId) => $"crm:branch:{branchId}:active-orders";
static string ActiveOrderKey(Guid orderId) => $"crm:order:{orderId}:state";

static async Task StoreActiveOrderStateAsync(IDistributedCache cache, HookahOrder order, Guid hookahId, DateTimeOffset? nextCoalChangeAt, CancellationToken cancellationToken)
{
    var state = new ActiveOrderRuntimeState(order.Id, order.BranchId, order.TableId, hookahId, order.ClientId, order.HookahMasterId, order.Status, order.TotalPrice, nextCoalChangeAt, DateTimeOffset.UtcNow);
    await cache.SetJsonAsync(ActiveOrderKey(order.Id), state, TimeSpan.FromHours(12), cancellationToken);

    var ids = await cache.GetJsonAsync<Guid[]>(ActiveOrdersIndexKey(order.BranchId), cancellationToken) ?? [];
    var nextIds = ids.Append(order.Id).Distinct().ToArray();
    await cache.SetJsonAsync(ActiveOrdersIndexKey(order.BranchId), nextIds, TimeSpan.FromHours(12), cancellationToken);
}

static async Task RemoveActiveOrderStateAsync(IDistributedCache cache, Guid branchId, Guid orderId, CancellationToken cancellationToken)
{
    await cache.RemoveAsync(ActiveOrderKey(orderId), cancellationToken);
    var ids = await cache.GetJsonAsync<Guid[]>(ActiveOrdersIndexKey(branchId), cancellationToken) ?? [];
    await cache.SetJsonAsync(ActiveOrdersIndexKey(branchId), ids.Where(id => id != orderId).ToArray(), TimeSpan.FromHours(12), cancellationToken);
}

static async Task MarkResourcesInUseAsync(Guid tableId, Guid hookahId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("order-service");
    (await client.PatchAsJsonAsync($"{branchBaseUrl}/api/tables/{tableId}/status", new UpdateTableStatusRequest("OCCUPIED"), cancellationToken)).EnsureSuccessStatusCode();
    (await client.PatchAsJsonAsync($"{branchBaseUrl}/api/hookahs/{hookahId}/status", new UpdateHookahStatusRequest(HookahStatuses.InUse), cancellationToken)).EnsureSuccessStatusCode();
}

static async Task<ResourceAvailabilityResult> CheckResourcesAvailableAsync(Guid tableId, Guid hookahId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("order-service");
    var table = await client.GetFromJsonAsync<TableDto>($"{branchBaseUrl}/api/tables/{tableId}", cancellationToken);
    if (table is null || !table.IsActive) return new(false, "Table is inactive or does not exist.");
    if (!string.Equals(table.Status, "FREE", StringComparison.OrdinalIgnoreCase)) return new(false, "Table is not free.");
    var hookah = await client.GetFromJsonAsync<HookahDto>($"{branchBaseUrl}/api/hookahs/{hookahId}", cancellationToken);
    if (hookah is null) return new(false, "Hookah does not exist.");
    if (!string.Equals(hookah.Status, HookahStatuses.Available, StringComparison.OrdinalIgnoreCase)) return new(false, "Hookah is not available.");
    return new(true, "Resources are available.");
}

static async Task ReleaseResourcesAsync(Guid tableId, Guid hookahId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("order-service");
    (await client.PatchAsJsonAsync($"{branchBaseUrl}/api/tables/{tableId}/status", new UpdateTableStatusRequest("FREE"), cancellationToken)).EnsureSuccessStatusCode();
    (await client.PatchAsJsonAsync($"{branchBaseUrl}/api/hookahs/{hookahId}/status", new UpdateHookahStatusRequest(HookahStatuses.Available), cancellationToken)).EnsureSuccessStatusCode();
}

static async Task<MixDto?> GetMixAsync(Guid mixId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var mixologyBaseUrl = configuration["Services:mixology-service:BaseUrl"] ?? "http://mixology-service:8080";
    var client = httpClientFactory.CreateClient("order-service");
    return await client.GetFromJsonAsync<MixDto>($"{mixologyBaseUrl}/api/mixes/{mixId}", cancellationToken);
}

static async Task<InventoryAvailabilityResponse> CheckMixAvailabilityAsync(Guid branchId, MixDto mix, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var inventoryBaseUrl = configuration["Services:inventory-service:BaseUrl"] ?? "http://inventory-service:8080";
    var client = httpClientFactory.CreateClient("order-service");
    var response = await client.PostAsJsonAsync($"{inventoryBaseUrl}/api/inventory/check", new InventoryAvailabilityRequest(branchId, mix.Items.Select(item => new InventoryAvailabilityRequestItem(item.TobaccoId, item.Grams)).ToArray()), cancellationToken);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<InventoryAvailabilityResponse>(cancellationToken))!;
}

public sealed record HookahOrder(Guid Id, Guid BranchId, Guid TableId, Guid? ClientId, Guid? HookahMasterId, Guid? WaiterId, Guid? BookingId, string Status, decimal TotalPrice, string? Comment, DateTimeOffset CreatedAt, DateTimeOffset? ServedAt, DateTimeOffset? CompletedAt, DateTimeOffset? NextCoalChangeAt, DateTimeOffset? InventoryWrittenOffAt, Guid? PaymentId, decimal PaidAmount, DateTimeOffset? PaidAt, IReadOnlyCollection<OrderItem> Items);
public sealed record OrderItem(Guid Id, Guid OrderId, Guid HookahId, Guid BowlId, Guid MixId, decimal Price, string Status);
public sealed record CreateOrderRequest(Guid BranchId, Guid TableId, Guid? ClientId, Guid HookahId, Guid BowlId, Guid MixId, Guid? BookingId, Guid? WaiterId, decimal? Price, string? Comment);
public sealed record UpdateOrderStatusRequest(string Status);
public sealed record AssignHookahMasterRequest(Guid HookahMasterId);
public sealed record CoalChangeRequest(DateTimeOffset? ChangedAt);
public sealed record CoalTimerState(Guid OrderId, DateTimeOffset LastChangedAt, DateTimeOffset NextCoalChangeAt);
public sealed record ActiveOrderRuntimeState(Guid OrderId, Guid BranchId, Guid TableId, Guid HookahId, Guid? ClientId, Guid? HookahMasterId, string Status, decimal TotalPrice, DateTimeOffset? NextCoalChangeAt, DateTimeOffset UpdatedAt);
public sealed record CancelOrderRequest(string Reason);
public sealed record OrderPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record MixDto(Guid Id, Guid BowlId, IReadOnlyCollection<MixItemDto> Items);
public sealed record MixItemDto(Guid Id, Guid TobaccoId, decimal Percent, decimal Grams);
public sealed record InventoryAvailabilityRequest(Guid BranchId, IReadOnlyCollection<InventoryAvailabilityRequestItem> Items);
public sealed record InventoryAvailabilityRequestItem(Guid TobaccoId, decimal RequiredGrams);
public sealed record InventoryAvailabilityResponse(Guid BranchId, bool IsAvailable, IReadOnlyCollection<InventoryAvailabilityItem> Items);
public sealed record InventoryAvailabilityItem(Guid TobaccoId, decimal RequiredGrams, decimal StockGrams, bool IsAvailable, decimal ShortageGrams);
public sealed record UpdateTableStatusRequest(string Status);
public sealed record UpdateHookahStatusRequest(string Status);
public sealed record TableDto(Guid Id, Guid HallId, Guid? ZoneId, string Name, int Capacity, string Status, decimal XPosition, decimal YPosition, bool IsActive);
public sealed record HookahDto(Guid Id, Guid BranchId, string Name, string Brand, string Model, string Status);
public sealed record ResourceAvailabilityResult(bool IsAvailable, string Message);
