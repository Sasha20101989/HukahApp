using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("order-service");
builder.Services.AddHttpClient();

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

app.MapPost("/api/orders", async (CreateOrderRequest request, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (request.MixId == Guid.Empty || request.BowlId == Guid.Empty || request.HookahId == Guid.Empty)
    {
        return HttpResults.Validation("Hookah, bowl and mix are required.");
    }

    var mix = await GetMixAsync(request.MixId, httpClientFactory, configuration, cancellationToken);
    if (mix is null)
    {
        return HttpResults.Validation("Mix must exist before creating an order.");
    }

    if (mix.BowlId != request.BowlId)
    {
        return HttpResults.Validation("Order bowl must match the selected mix bowl.");
    }

    var availability = await CheckMixAvailabilityAsync(request.BranchId, mix, httpClientFactory, configuration, cancellationToken);
    if (!availability.IsAvailable)
    {
        return Results.Conflict(new
        {
            code = "mix_unavailable",
            message = "Cannot create order because the mix is not available in branch inventory.",
            unavailableItems = availability.Items.Where(item => !item.IsAvailable)
        });
    }

    var orderId = Guid.NewGuid();
    var item = new OrderItem(Guid.NewGuid(), orderId, request.HookahId, request.BowlId, request.MixId, request.Price ?? 850m, OrderStatuses.New);
    var order = new HookahOrder(orderId, request.BranchId, request.TableId, request.ClientId, null, request.WaiterId, request.BookingId, OrderStatuses.New, item.Price, request.Comment, DateTimeOffset.UtcNow, null, null, null, [item]);
    orders[order.Id] = order;

    await MarkResourcesInUseAsync(request.TableId, request.HookahId, httpClientFactory, configuration, cancellationToken);
    await events.PublishAsync(new OrderCreated(order.Id, order.BranchId, order.TableId, order.ClientId, item.MixId, DateTimeOffset.UtcNow));
    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapGet("/api/orders/{id:guid}", (Guid id) =>
    orders.TryGetValue(id, out var order) ? Results.Ok(order) : HttpResults.NotFound("Order", id));

app.MapPatch("/api/orders/{id:guid}/status", async (Guid id, UpdateOrderStatusRequest request, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!orders.TryGetValue(id, out var order))
    {
        return HttpResults.NotFound("Order", id);
    }

    var normalized = request.Status.ToUpperInvariant();
    var servedAt = normalized == OrderStatuses.Served ? DateTimeOffset.UtcNow : order.ServedAt;
    var completedAt = normalized == OrderStatuses.Completed ? DateTimeOffset.UtcNow : order.CompletedAt;
    var nextCoalChangeAt = normalized == OrderStatuses.Smoking ? DateTimeOffset.UtcNow.AddMinutes(20) : order.NextCoalChangeAt;
    var updated = order with { Status = normalized, ServedAt = servedAt, CompletedAt = completedAt, NextCoalChangeAt = nextCoalChangeAt };
    orders[id] = updated;

    await events.PublishAsync(new OrderStatusChanged(id, normalized, DateTimeOffset.UtcNow));
    if (normalized == OrderStatuses.Served)
    {
        var item = updated.Items.First();
        await WriteOffMixAsync(updated.BranchId, updated.Id, item.MixId, httpClientFactory, configuration, cancellationToken);
        await events.PublishAsync(new OrderServed(updated.Id, updated.BranchId, item.MixId, item.BowlId, servedAt!.Value, DateTimeOffset.UtcNow));
    }
    if (normalized == OrderStatuses.Completed)
    {
        var item = updated.Items.First();
        await ReleaseResourcesAsync(updated.TableId, item.HookahId, httpClientFactory, configuration, cancellationToken);
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

    var order = orders[id];
    orders[id] = order with
    {
        Status = OrderStatuses.Smoking,
        NextCoalChangeAt = change.ChangedAt.AddMinutes(20)
    };

    return Results.Created($"/api/orders/{id}/coal-change/{change.Id}", change);
});

app.MapGet("/api/orders/{id:guid}/coal-timer", (Guid id) =>
{
    if (!orders.TryGetValue(id, out var order))
    {
        return HttpResults.NotFound("Order", id);
    }

    return Results.Ok(new
    {
        order.Id,
        order.Status,
        order.NextCoalChangeAt,
        changes = coalChanges.Where(change => change.OrderId == id).OrderByDescending(change => change.ChangedAt)
    });
});

app.MapDelete("/api/orders/{id:guid}", async (Guid id, CancelOrderRequest request, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!orders.TryGetValue(id, out var order))
    {
        return HttpResults.NotFound("Order", id);
    }

    orders[id] = order with { Status = OrderStatuses.Cancelled };
    var item = order.Items.First();
    await ReleaseResourcesAsync(order.TableId, item.HookahId, httpClientFactory, configuration, cancellationToken);
    await events.PublishAsync(new OrderStatusChanged(id, OrderStatuses.Cancelled, DateTimeOffset.UtcNow));
    return Results.Ok(new { id, status = OrderStatuses.Cancelled, request.Reason });
});

app.Run();

static async Task WriteOffMixAsync(Guid branchId, Guid orderId, Guid mixId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var inventoryBaseUrl = configuration["Services:inventory-service:BaseUrl"] ?? "http://inventory-service:8080";
    var client = httpClientFactory.CreateClient("order-service");

    var mix = await GetMixAsync(mixId, httpClientFactory, configuration, cancellationToken);
    if (mix is null)
    {
        throw new InvalidOperationException($"Mix '{mixId}' was not found.");
    }

    foreach (var item in mix.Items)
    {
        var response = await client.PostAsJsonAsync(
            $"{inventoryBaseUrl}/api/inventory/out",
            new InventoryOutRequest(branchId, item.TobaccoId, item.Grams, "Order served", orderId, null),
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}

static async Task MarkResourcesInUseAsync(Guid tableId, Guid hookahId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("order-service");

    var tableResponse = await client.PatchAsJsonAsync($"{branchBaseUrl}/api/tables/{tableId}/status", new UpdateTableStatusRequest("OCCUPIED"), cancellationToken);
    tableResponse.EnsureSuccessStatusCode();

    var hookahResponse = await client.PatchAsJsonAsync($"{branchBaseUrl}/api/hookahs/{hookahId}/status", new UpdateHookahStatusRequest(HookahStatuses.InUse), cancellationToken);
    hookahResponse.EnsureSuccessStatusCode();
}

static async Task ReleaseResourcesAsync(Guid tableId, Guid hookahId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("order-service");

    var tableResponse = await client.PatchAsJsonAsync($"{branchBaseUrl}/api/tables/{tableId}/status", new UpdateTableStatusRequest("FREE"), cancellationToken);
    tableResponse.EnsureSuccessStatusCode();

    var hookahResponse = await client.PatchAsJsonAsync($"{branchBaseUrl}/api/hookahs/{hookahId}/status", new UpdateHookahStatusRequest(HookahStatuses.Available), cancellationToken);
    hookahResponse.EnsureSuccessStatusCode();
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
    var response = await client.PostAsJsonAsync(
        $"{inventoryBaseUrl}/api/inventory/check",
        new InventoryAvailabilityRequest(branchId, mix.Items.Select(item => new InventoryAvailabilityRequestItem(item.TobaccoId, item.Grams)).ToArray()),
        cancellationToken);

    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<InventoryAvailabilityResponse>(cancellationToken))!;
}

public sealed record HookahOrder(Guid Id, Guid BranchId, Guid TableId, Guid? ClientId, Guid? HookahMasterId, Guid? WaiterId, Guid? BookingId, string Status, decimal TotalPrice, string? Comment, DateTimeOffset CreatedAt, DateTimeOffset? ServedAt, DateTimeOffset? CompletedAt, DateTimeOffset? NextCoalChangeAt, IReadOnlyCollection<OrderItem> Items);
public sealed record OrderItem(Guid Id, Guid OrderId, Guid HookahId, Guid BowlId, Guid MixId, decimal Price, string Status);
public sealed record CoalChange(Guid Id, Guid OrderId, DateTimeOffset ChangedAt);
public sealed record CreateOrderRequest(Guid BranchId, Guid TableId, Guid? ClientId, Guid HookahId, Guid BowlId, Guid MixId, Guid? BookingId, Guid? WaiterId, decimal? Price, string? Comment);
public sealed record UpdateOrderStatusRequest(string Status);
public sealed record AssignHookahMasterRequest(Guid HookahMasterId);
public sealed record CoalChangeRequest(DateTimeOffset? ChangedAt);
public sealed record CancelOrderRequest(string Reason);
public sealed record MixDto(Guid Id, Guid BowlId, IReadOnlyCollection<MixItemDto> Items);
public sealed record MixItemDto(Guid Id, Guid TobaccoId, decimal Percent, decimal Grams);
public sealed record InventoryAvailabilityRequest(Guid BranchId, IReadOnlyCollection<InventoryAvailabilityRequestItem> Items);
public sealed record InventoryAvailabilityRequestItem(Guid TobaccoId, decimal RequiredGrams);
public sealed record InventoryAvailabilityResponse(Guid BranchId, bool IsAvailable, IReadOnlyCollection<InventoryAvailabilityItem> Items);
public sealed record InventoryAvailabilityItem(Guid TobaccoId, decimal RequiredGrams, decimal StockGrams, bool IsAvailable, decimal ShortageGrams);
public sealed record InventoryOutRequest(Guid BranchId, Guid TobaccoId, decimal AmountGrams, string Reason, Guid? OrderId, Guid? CreatedBy);
public sealed record UpdateTableStatusRequest(string Status);
public sealed record UpdateHookahStatusRequest(string Status);
