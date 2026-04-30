using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("inventory-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var items = new Dictionary<(Guid BranchId, Guid TobaccoId), InventoryItem>();
var movements = new List<InventoryMovement>();
SeedInventory(items);

app.MapGet("/api/inventory", (Guid? branchId, bool? lowStockOnly) =>
{
    var query = items.Values.AsEnumerable();
    if (branchId is not null)
    {
        query = query.Where(item => item.BranchId == branchId);
    }
    if (lowStockOnly == true)
    {
        query = query.Where(item => item.StockGrams < item.MinStockGrams);
    }

    return Results.Ok(query.OrderBy(item => item.BranchId).ThenBy(item => item.TobaccoId));
});

app.MapPost("/api/inventory/check", (InventoryAvailabilityRequest request) =>
{
    if (request.Items.Count == 0)
    {
        return HttpResults.Validation("Availability check must contain at least one tobacco item.");
    }

    var checkedItems = request.Items.Select(required =>
    {
        var stock = items.TryGetValue((request.BranchId, required.TobaccoId), out var item)
            ? item.StockGrams
            : 0m;

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

app.MapPost("/api/inventory/in", async (InventoryInRequest request, IEventPublisher events) =>
{
    var item = GetOrCreateItem(items, request.BranchId, request.TobaccoId);
    var updated = item with { StockGrams = item.StockGrams + request.AmountGrams, UpdatedAt = DateTimeOffset.UtcNow };
    items[(request.BranchId, request.TobaccoId)] = updated;

    movements.Add(new InventoryMovement(Guid.NewGuid(), request.BranchId, request.TobaccoId, "IN", request.AmountGrams, request.Comment ?? $"Supplier: {request.Supplier}", null, null, DateTimeOffset.UtcNow));
    await PublishLowStockIfNeeded(updated, events);

    return Results.Ok(updated);
});

app.MapPost("/api/inventory/out", async (InventoryOutRequest request, IEventPublisher events) =>
{
    var item = GetOrCreateItem(items, request.BranchId, request.TobaccoId);
    if (item.StockGrams < request.AmountGrams)
    {
        return HttpResults.Conflict("Not enough tobacco in stock.");
    }

    var updated = item with { StockGrams = item.StockGrams - request.AmountGrams, UpdatedAt = DateTimeOffset.UtcNow };
    items[(request.BranchId, request.TobaccoId)] = updated;

    movements.Add(new InventoryMovement(Guid.NewGuid(), request.BranchId, request.TobaccoId, "OUT", request.AmountGrams, request.Reason, request.OrderId, request.CreatedBy, DateTimeOffset.UtcNow));
    await events.PublishAsync(new InventoryWrittenOff(request.BranchId, request.TobaccoId, request.OrderId, request.AmountGrams, DateTimeOffset.UtcNow));
    await PublishLowStockIfNeeded(updated, events);

    return Results.Ok(updated);
});

app.MapPost("/api/inventory/adjustment", async (InventoryAdjustmentRequest request, IEventPublisher events) =>
{
    var item = GetOrCreateItem(items, request.BranchId, request.TobaccoId);
    var delta = request.NewStockGrams - item.StockGrams;
    var updated = item with { StockGrams = request.NewStockGrams, UpdatedAt = DateTimeOffset.UtcNow };
    items[(request.BranchId, request.TobaccoId)] = updated;

    movements.Add(new InventoryMovement(Guid.NewGuid(), request.BranchId, request.TobaccoId, "ADJUSTMENT", delta, "Manual adjustment", null, request.CreatedBy, DateTimeOffset.UtcNow));
    await PublishLowStockIfNeeded(updated, events);

    return Results.Ok(updated);
});

app.MapGet("/api/inventory/movements", (Guid? branchId, Guid? tobaccoId, DateOnly? from, DateOnly? to) =>
{
    var query = movements.AsEnumerable();
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

    return Results.Ok(query.OrderByDescending(movement => movement.CreatedAt));
});

app.Run();

static InventoryItem GetOrCreateItem(IDictionary<(Guid BranchId, Guid TobaccoId), InventoryItem> items, Guid branchId, Guid tobaccoId)
{
    var key = (branchId, tobaccoId);
    if (items.TryGetValue(key, out var item))
    {
        return item;
    }

    item = new InventoryItem(Guid.NewGuid(), branchId, tobaccoId, 0, 50, DateTimeOffset.UtcNow);
    items[key] = item;
    return item;
}

static async Task PublishLowStockIfNeeded(InventoryItem item, IEventPublisher events)
{
    if (item.StockGrams < item.MinStockGrams)
    {
        await events.PublishAsync(new LowStockDetected(item.BranchId, item.TobaccoId, item.StockGrams, item.MinStockGrams, DateTimeOffset.UtcNow));
    }
}

static void SeedInventory(IDictionary<(Guid BranchId, Guid TobaccoId), InventoryItem> items)
{
    var branchId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    foreach (var tobaccoId in new[]
    {
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        Guid.Parse("60000000-0000-0000-0000-000000000002"),
        Guid.Parse("60000000-0000-0000-0000-000000000003")
    })
    {
        items[(branchId, tobaccoId)] = new InventoryItem(Guid.NewGuid(), branchId, tobaccoId, 250, 50, DateTimeOffset.UtcNow);
    }
}

public sealed record InventoryItem(Guid Id, Guid BranchId, Guid TobaccoId, decimal StockGrams, decimal MinStockGrams, DateTimeOffset UpdatedAt);
public sealed record InventoryMovement(Guid Id, Guid BranchId, Guid TobaccoId, string Type, decimal AmountGrams, string? Reason, Guid? OrderId, Guid? CreatedBy, DateTimeOffset CreatedAt);
public sealed record InventoryAvailabilityRequest(Guid BranchId, IReadOnlyCollection<InventoryAvailabilityRequestItem> Items);
public sealed record InventoryAvailabilityRequestItem(Guid TobaccoId, decimal RequiredGrams);
public sealed record InventoryAvailabilityResponse(Guid BranchId, bool IsAvailable, IReadOnlyCollection<InventoryAvailabilityItem> Items);
public sealed record InventoryAvailabilityItem(Guid TobaccoId, decimal RequiredGrams, decimal StockGrams, bool IsAvailable, decimal ShortageGrams);
public sealed record InventoryInRequest(Guid BranchId, Guid TobaccoId, decimal AmountGrams, decimal CostPerGram, string? Supplier, string? Comment);
public sealed record InventoryOutRequest(Guid BranchId, Guid TobaccoId, decimal AmountGrams, string Reason, Guid? OrderId, Guid? CreatedBy);
public sealed record InventoryAdjustmentRequest(Guid BranchId, Guid TobaccoId, decimal NewStockGrams, Guid? CreatedBy);
