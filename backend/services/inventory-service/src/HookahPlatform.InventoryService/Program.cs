using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.InventoryService.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("inventory-service");
builder.AddPostgresDbContext<InventoryDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<InventoryDbContext>("inventory-service");

app.MapGet("/api/inventory", async (Guid? branchId, bool? lowStockOnly, InventoryDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.InventoryItems.AsNoTracking();
    if (branchId is not null)
    {
        query = query.Where(item => item.BranchId == branchId);
    }
    if (lowStockOnly == true)
    {
        query = query.Where(item => item.StockGrams < item.MinStockGrams);
    }

    return Results.Ok(await query.OrderBy(item => item.BranchId).ThenBy(item => item.TobaccoId).ToListAsync(cancellationToken));
});

app.MapPost("/api/inventory/check", async (InventoryAvailabilityRequest request, InventoryDbContext db, CancellationToken cancellationToken) =>
{
    if (request.Items.Count == 0)
    {
        return HttpResults.Validation("Availability check must contain at least one tobacco item.");
    }

    var tobaccoIds = request.Items.Select(item => item.TobaccoId).ToArray();
    var stockByTobaccoId = await db.InventoryItems
        .AsNoTracking()
        .Where(item => item.BranchId == request.BranchId && tobaccoIds.Contains(item.TobaccoId))
        .ToDictionaryAsync(item => item.TobaccoId, item => item.StockGrams, cancellationToken);

    var checkedItems = request.Items.Select(required =>
    {
        var stock = stockByTobaccoId.TryGetValue(required.TobaccoId, out var stockGrams) ? stockGrams : 0m;

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

app.MapPost("/api/inventory/in", async (InventoryInRequest request, InventoryDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var item = await GetOrCreateItemAsync(db, request.BranchId, request.TobaccoId, cancellationToken);
    item.StockGrams += request.AmountGrams;
    item.UpdatedAt = DateTimeOffset.UtcNow;

    db.InventoryMovements.Add(new InventoryMovementEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, TobaccoId = request.TobaccoId, Type = "IN", AmountGrams = request.AmountGrams, Reason = request.Comment ?? $"Supplier: {request.Supplier}", OrderId = null, CreatedBy = null, CreatedAt = DateTimeOffset.UtcNow });
    var outboxEvents = BuildLowStockEvents(item);
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    return Results.Ok(item);
});

app.MapPost("/api/inventory/out", async (InventoryOutRequest request, InventoryDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var item = await GetOrCreateItemAsync(db, request.BranchId, request.TobaccoId, cancellationToken);
    if (item.StockGrams < request.AmountGrams)
    {
        return HttpResults.Conflict("Not enough tobacco in stock.");
    }

    item.StockGrams -= request.AmountGrams;
    item.UpdatedAt = DateTimeOffset.UtcNow;

    db.InventoryMovements.Add(new InventoryMovementEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, TobaccoId = request.TobaccoId, Type = "OUT", AmountGrams = request.AmountGrams, Reason = request.Reason, OrderId = request.OrderId, CreatedBy = request.CreatedBy, CreatedAt = DateTimeOffset.UtcNow });
    var outboxEvents = new List<IIntegrationEvent>
    {
        new InventoryWrittenOff(request.BranchId, request.TobaccoId, request.OrderId, request.AmountGrams, DateTimeOffset.UtcNow)
    };
    outboxEvents.AddRange(BuildLowStockEvents(item));
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    return Results.Ok(item);
});

app.MapPost("/api/inventory/adjustment", async (InventoryAdjustmentRequest request, InventoryDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var item = await GetOrCreateItemAsync(db, request.BranchId, request.TobaccoId, cancellationToken);
    var delta = request.NewStockGrams - item.StockGrams;
    item.StockGrams = request.NewStockGrams;
    item.UpdatedAt = DateTimeOffset.UtcNow;

    db.InventoryMovements.Add(new InventoryMovementEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, TobaccoId = request.TobaccoId, Type = "ADJUSTMENT", AmountGrams = delta, Reason = "Manual adjustment", OrderId = null, CreatedBy = request.CreatedBy, CreatedAt = DateTimeOffset.UtcNow });
    var outboxEvents = BuildLowStockEvents(item);
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);

    return Results.Ok(item);
});

app.MapGet("/api/inventory/movements", async (Guid? branchId, Guid? tobaccoId, DateOnly? from, DateOnly? to, InventoryDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.InventoryMovements.AsNoTracking();
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

    return Results.Ok(await query.OrderByDescending(movement => movement.CreatedAt).ToListAsync(cancellationToken));
});

app.Run();

static async Task<InventoryItemEntity> GetOrCreateItemAsync(InventoryDbContext db, Guid branchId, Guid tobaccoId, CancellationToken cancellationToken)
{
    var item = await db.InventoryItems.FirstOrDefaultAsync(candidate => candidate.BranchId == branchId && candidate.TobaccoId == tobaccoId, cancellationToken);
    if (item is not null)
    {
        return item;
    }

    item = new InventoryItemEntity { Id = Guid.NewGuid(), BranchId = branchId, TobaccoId = tobaccoId, StockGrams = 0, MinStockGrams = 50, UpdatedAt = DateTimeOffset.UtcNow };
    db.InventoryItems.Add(item);
    return item;
}

static IReadOnlyCollection<IIntegrationEvent> BuildLowStockEvents(InventoryItemEntity item)
{
    if (item.StockGrams < item.MinStockGrams)
    {
        return [new LowStockDetected(item.BranchId, item.TobaccoId, item.StockGrams, item.MinStockGrams, DateTimeOffset.UtcNow)];
    }

    return [];
}

public sealed record InventoryAvailabilityRequest(Guid BranchId, IReadOnlyCollection<InventoryAvailabilityRequestItem> Items);
public sealed record InventoryAvailabilityRequestItem(Guid TobaccoId, decimal RequiredGrams);
public sealed record InventoryAvailabilityResponse(Guid BranchId, bool IsAvailable, IReadOnlyCollection<InventoryAvailabilityItem> Items);
public sealed record InventoryAvailabilityItem(Guid TobaccoId, decimal RequiredGrams, decimal StockGrams, bool IsAvailable, decimal ShortageGrams);
public sealed record InventoryInRequest(Guid BranchId, Guid TobaccoId, decimal AmountGrams, decimal CostPerGram, string? Supplier, string? Comment);
public sealed record InventoryOutRequest(Guid BranchId, Guid TobaccoId, decimal AmountGrams, string Reason, Guid? OrderId, Guid? CreatedBy);
public sealed record InventoryAdjustmentRequest(Guid BranchId, Guid TobaccoId, decimal NewStockGrams, Guid? CreatedBy);
