using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using HookahPlatform.MixologyService.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("mixology-service");
builder.AddPostgresDbContext<MixologyDbContext>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<MixologyDbContext>("mixology-service");

app.MapGet("/api/bowls", async (MixologyDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Bowls.AsNoTracking().Where(bowl => bowl.IsActive).OrderBy(bowl => bowl.Name).ToListAsync(cancellationToken)));

app.MapPost("/api/bowls", async (CreateBowlRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var bowl = new BowlEntity { Id = Guid.NewGuid(), Name = request.Name, Type = request.Type, CapacityGrams = request.CapacityGrams, RecommendedStrength = request.RecommendedStrength, AverageSmokeMinutes = request.AverageSmokeMinutes, IsActive = true };
    db.Bowls.Add(bowl);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/bowls/{bowl.Id}", bowl);
});

app.MapPatch("/api/bowls/{id:guid}", async (Guid id, UpdateBowlRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var bowl = await db.Bowls.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (bowl is null) return HttpResults.NotFound("Bowl", id);
    bowl.Name = request.Name ?? bowl.Name;
    bowl.Type = request.Type ?? bowl.Type;
    bowl.CapacityGrams = request.CapacityGrams ?? bowl.CapacityGrams;
    bowl.RecommendedStrength = request.RecommendedStrength ?? bowl.RecommendedStrength;
    bowl.AverageSmokeMinutes = request.AverageSmokeMinutes ?? bowl.AverageSmokeMinutes;
    bowl.IsActive = request.IsActive ?? bowl.IsActive;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(bowl);
});

app.MapDelete("/api/bowls/{id:guid}", async (Guid id, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var bowl = await db.Bowls.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (bowl is null) return HttpResults.NotFound("Bowl", id);
    bowl.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/tobaccos", async (string? brand, string? strength, string? category, bool? isActive, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Tobaccos.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(brand)) query = query.Where(tobacco => tobacco.Brand == brand);
    if (!string.IsNullOrWhiteSpace(strength)) query = query.Where(tobacco => tobacco.Strength == strength);
    if (!string.IsNullOrWhiteSpace(category)) query = query.Where(tobacco => tobacco.Category == category);
    if (isActive is not null) query = query.Where(tobacco => tobacco.IsActive == isActive);
    return Results.Ok(await query.OrderBy(tobacco => tobacco.Brand).ThenBy(tobacco => tobacco.Flavor).ToListAsync(cancellationToken));
});

app.MapPost("/api/tobaccos", async (CreateTobaccoRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var tobacco = new TobaccoEntity { Id = Guid.NewGuid(), Brand = request.Brand, Line = request.Line, Flavor = request.Flavor, Strength = request.Strength, Category = request.Category, Description = request.Description, CostPerGram = request.CostPerGram, IsActive = true, PhotoUrl = request.PhotoUrl };
    db.Tobaccos.Add(tobacco);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/tobaccos/{tobacco.Id}", tobacco);
});

app.MapPatch("/api/tobaccos/{id:guid}", async (Guid id, UpdateTobaccoRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var tobacco = await db.Tobaccos.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (tobacco is null) return HttpResults.NotFound("Tobacco", id);
    tobacco.Brand = request.Brand ?? tobacco.Brand;
    tobacco.Line = request.Line ?? tobacco.Line;
    tobacco.Flavor = request.Flavor ?? tobacco.Flavor;
    tobacco.Strength = request.Strength ?? tobacco.Strength;
    tobacco.Category = request.Category ?? tobacco.Category;
    tobacco.Description = request.Description ?? tobacco.Description;
    tobacco.CostPerGram = request.CostPerGram ?? tobacco.CostPerGram;
    tobacco.IsActive = request.IsActive ?? tobacco.IsActive;
    tobacco.PhotoUrl = request.PhotoUrl ?? tobacco.PhotoUrl;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(tobacco);
});

app.MapDelete("/api/tobaccos/{id:guid}", async (Guid id, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var tobacco = await db.Tobaccos.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (tobacco is null) return HttpResults.NotFound("Tobacco", id);
    tobacco.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/mixes", async (string? strength, string? tasteProfile, Guid? bowlId, bool? availableOnly, Guid? branchId, bool? publicOnly, MixologyDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var query = db.Mixes.AsNoTracking().Where(mix => mix.IsActive);
    if (!string.IsNullOrWhiteSpace(strength)) query = query.Where(mix => mix.Strength == strength);
    if (!string.IsNullOrWhiteSpace(tasteProfile)) query = query.Where(mix => mix.TasteProfile == tasteProfile);
    if (bowlId is not null) query = query.Where(mix => mix.BowlId == bowlId);
    if (publicOnly == true) query = query.Where(mix => mix.IsPublic);

    var mixes = await LoadMixDtosAsync(query.OrderBy(mix => mix.Name), db, cancellationToken);
    if (availableOnly == true)
    {
        var activeTobaccos = await db.Tobaccos.AsNoTracking().Where(tobacco => tobacco.IsActive).Select(tobacco => tobacco.Id).ToListAsync(cancellationToken);
        mixes = mixes.Where(mix => mix.Items.All(item => activeTobaccos.Contains(item.TobaccoId))).ToList();
        if (branchId is not null)
        {
            mixes = (await FilterAvailableMixesAsync(branchId.Value, mixes, httpClientFactory, configuration, cancellationToken)).ToList();
        }
    }

    return publicOnly == true ? Results.Ok(mixes.Select(ToPublicMix)) : Results.Ok(mixes);
});

app.MapGet("/api/mixes/{id:guid}", async (Guid id, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var mix = await db.Mixes.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (mix is null) return HttpResults.NotFound("Mix", id);
    var items = await db.MixItems.AsNoTracking().Where(item => item.MixId == id).OrderBy(item => item.Id).ToListAsync(cancellationToken);
    return Results.Ok(ToMixDto(mix, items));
});

app.MapPost("/api/mixes/calculate", async (CalculateMixRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var result = await CalculateMixAsync(request.BowlId, request.Items, db, cancellationToken);
    return result.Error is not null ? HttpResults.Validation(result.Error) : Results.Ok(result.Value);
});

app.MapPost("/api/mixes", async (CreateMixRequest request, MixologyDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var result = await CalculateMixAsync(request.BowlId, request.Items, db, cancellationToken);
    if (result.Error is not null || result.Value is null) return HttpResults.Validation(result.Error ?? "Mix calculation failed.");

    var price = request.Price ?? Math.Ceiling(result.Value.Cost * 3.2m / 10m) * 10m;
    var mix = new MixEntity { Id = Guid.NewGuid(), Name = request.Name, Description = request.Description, BowlId = request.BowlId, Strength = request.Strength, TasteProfile = request.TasteProfile, TotalGrams = result.Value.TotalGrams, Price = price, Cost = result.Value.Cost, Margin = price - result.Value.Cost, IsPublic = request.IsPublic, IsActive = true, CreatedBy = request.CreatedBy, CreatedAt = DateTimeOffset.UtcNow };
    db.Mixes.Add(mix);
    foreach (var item in result.Value.Items)
    {
        db.MixItems.Add(new MixItemEntity { Id = Guid.NewGuid(), MixId = mix.Id, TobaccoId = item.TobaccoId, Percent = item.Percent, Grams = item.Grams });
    }
    var created = new MixCreated(mix.Id, mix.Name, mix.BowlId, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(created);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, created, outboxMessage, cancellationToken);

    var items = await db.MixItems.AsNoTracking().Where(item => item.MixId == mix.Id).ToListAsync(cancellationToken);
    return Results.Created($"/api/mixes/{mix.Id}", ToMixDto(mix, items));
});

app.MapPost("/api/mixes/recommend", async (RecommendMixRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Mixes.AsNoTracking().Where(mix => mix.IsActive && mix.Strength == request.Strength && mix.TasteProfile == request.TasteProfile && mix.BowlId == request.BowlId).OrderByDescending(mix => mix.Margin).Take(10);
    var mixes = await LoadMixDtosAsync(query, db, cancellationToken);
    return Results.Ok(mixes);
});

app.MapPatch("/api/mixes/{id:guid}", async (Guid id, UpdateMixRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var mix = await db.Mixes.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (mix is null) return HttpResults.NotFound("Mix", id);
    if (request.BowlId is not null || request.Items is not null)
    {
        var nextBowlId = request.BowlId ?? mix.BowlId;
        var nextItems = request.Items ?? await db.MixItems.AsNoTracking()
            .Where(item => item.MixId == id)
            .Select(item => new MixInputItem(item.TobaccoId, item.Percent))
            .ToListAsync(cancellationToken);
        var result = await CalculateMixAsync(nextBowlId, nextItems, db, cancellationToken);
        if (result.Error is not null || result.Value is null) return HttpResults.Validation(result.Error ?? "Mix calculation failed.");
        mix.BowlId = nextBowlId;
        mix.TotalGrams = result.Value.TotalGrams;
        mix.Cost = result.Value.Cost;
        db.MixItems.RemoveRange(await db.MixItems.Where(item => item.MixId == id).ToListAsync(cancellationToken));
        foreach (var item in result.Value.Items)
        {
            db.MixItems.Add(new MixItemEntity { Id = Guid.NewGuid(), MixId = mix.Id, TobaccoId = item.TobaccoId, Percent = item.Percent, Grams = item.Grams });
        }
    }
    mix.Name = request.Name ?? mix.Name;
    mix.Description = request.Description ?? mix.Description;
    mix.Strength = request.Strength ?? mix.Strength;
    mix.TasteProfile = request.TasteProfile ?? mix.TasteProfile;
    mix.IsPublic = request.IsPublic ?? mix.IsPublic;
    mix.IsActive = request.IsActive ?? mix.IsActive;
    if (request.Price is not null)
    {
        mix.Price = request.Price.Value;
    }
    mix.Margin = mix.Price - mix.Cost;
    await db.SaveChangesAsync(cancellationToken);
    var items = await db.MixItems.AsNoTracking().Where(item => item.MixId == mix.Id).ToListAsync(cancellationToken);
    return Results.Ok(ToMixDto(mix, items));
});

app.MapPatch("/api/mixes/{id:guid}/visibility", async (Guid id, UpdateMixVisibilityRequest request, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var mix = await db.Mixes.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (mix is null) return HttpResults.NotFound("Mix", id);
    mix.IsPublic = request.IsPublic;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(mix);
});

app.MapDelete("/api/mixes/{id:guid}", async (Guid id, MixologyDbContext db, CancellationToken cancellationToken) =>
{
    var mix = await db.Mixes.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (mix is null) return HttpResults.NotFound("Mix", id);
    mix.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.Run();

static async Task<List<Mix>> LoadMixDtosAsync(IQueryable<MixEntity> query, MixologyDbContext db, CancellationToken cancellationToken)
{
    var mixEntities = await query.ToListAsync(cancellationToken);
    var mixIds = mixEntities.Select(mix => mix.Id).ToArray();
    var itemGroups = await db.MixItems.AsNoTracking().Where(item => mixIds.Contains(item.MixId)).GroupBy(item => item.MixId).ToDictionaryAsync(group => group.Key, group => group.ToList(), cancellationToken);
    return mixEntities.Select(mix => ToMixDto(mix, itemGroups.TryGetValue(mix.Id, out var items) ? items : [])).ToList();
}

static Mix ToMixDto(MixEntity mix, IReadOnlyCollection<MixItemEntity> items)
{
    return new Mix(mix.Id, mix.Name, mix.Description, mix.BowlId, mix.Strength, mix.TasteProfile, mix.TotalGrams, mix.Price, mix.Cost, mix.Margin, mix.IsPublic, mix.IsActive, mix.CreatedBy, mix.CreatedAt, items.Select(item => new MixItem(item.Id, item.TobaccoId, item.Percent, item.Grams)).ToArray());
}

static PublicMix ToPublicMix(Mix mix)
{
    return new PublicMix(mix.Id, mix.Name, mix.Description, mix.BowlId, mix.Strength, mix.TasteProfile, mix.TotalGrams, mix.Price, mix.IsPublic, mix.Items);
}

static async Task<IEnumerable<Mix>> FilterAvailableMixesAsync(Guid branchId, IReadOnlyCollection<Mix> mixes, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var inventoryBaseUrl = configuration["Services:inventory-service:BaseUrl"] ?? "http://inventory-service:8080";
    var client = httpClientFactory.CreateClient("mixology-service");
    var available = new List<Mix>();

    foreach (var mix in mixes)
    {
        var response = await client.PostAsJsonAsync($"{inventoryBaseUrl}/api/inventory/check", new InventoryAvailabilityRequest(branchId, mix.Items.Select(item => new InventoryAvailabilityRequestItem(item.TobaccoId, item.Grams)).ToArray()), cancellationToken);
        response.EnsureSuccessStatusCode();
        var availability = await response.Content.ReadFromJsonAsync<InventoryAvailabilityResponse>(cancellationToken);
        if (availability?.IsAvailable == true) available.Add(mix);
    }

    return available;
}

static async Task<CalculationResult> CalculateMixAsync(Guid bowlId, IReadOnlyCollection<MixInputItem> items, MixologyDbContext db, CancellationToken cancellationToken)
{
    var bowl = await db.Bowls.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == bowlId, cancellationToken);
    if (bowl is null || !bowl.IsActive) return new("Bowl must exist and be active.", null);
    if (items.Count == 0) return new("Mix must contain at least one tobacco.", null);
    if (!DomainRules.PercentSumIsValid(items.Select(item => item.Percent))) return new("Mix item percent sum must be exactly 100.", null);

    var tobaccoIds = items.Select(item => item.TobaccoId).ToArray();
    var tobaccos = await db.Tobaccos.AsNoTracking().Where(tobacco => tobaccoIds.Contains(tobacco.Id)).ToDictionaryAsync(tobacco => tobacco.Id, cancellationToken);
    var calculatedItems = new List<CalculatedMixItem>();
    decimal cost = 0;

    foreach (var item in items)
    {
        if (!tobaccos.TryGetValue(item.TobaccoId, out var tobacco) || !tobacco.IsActive) return new($"Tobacco '{item.TobaccoId}' must exist and be active.", null);
        var grams = DomainRules.CalculateGrams(bowl.CapacityGrams, item.Percent);
        cost += grams * tobacco.CostPerGram;
        calculatedItems.Add(new CalculatedMixItem(item.TobaccoId, item.Percent, grams));
    }

    return new(null, new CalculatedMix(bowl.CapacityGrams, Math.Round(cost, 2), calculatedItems));
}

public sealed record Mix(Guid Id, string Name, string? Description, Guid BowlId, string Strength, string TasteProfile, decimal TotalGrams, decimal Price, decimal Cost, decimal Margin, bool IsPublic, bool IsActive, Guid? CreatedBy, DateTimeOffset CreatedAt, IReadOnlyCollection<MixItem> Items);
public sealed record MixItem(Guid Id, Guid TobaccoId, decimal Percent, decimal Grams);
public sealed record PublicMix(Guid Id, string Name, string? Description, Guid BowlId, string Strength, string TasteProfile, decimal TotalGrams, decimal Price, bool IsPublic, IReadOnlyCollection<MixItem> Items);
public sealed record CreateBowlRequest(string Name, string Type, decimal CapacityGrams, string RecommendedStrength, int AverageSmokeMinutes);
public sealed record UpdateBowlRequest(string? Name, string? Type, decimal? CapacityGrams, string? RecommendedStrength, int? AverageSmokeMinutes, bool? IsActive);
public sealed record CreateTobaccoRequest(string Brand, string Line, string Flavor, string Strength, string Category, string? Description, decimal CostPerGram, string? PhotoUrl);
public sealed record UpdateTobaccoRequest(string? Brand, string? Line, string? Flavor, string? Strength, string? Category, string? Description, decimal? CostPerGram, bool? IsActive, string? PhotoUrl);
public sealed record MixInputItem(Guid TobaccoId, decimal Percent);
public sealed record CalculateMixRequest(Guid BowlId, IReadOnlyCollection<MixInputItem> Items);
public sealed record CreateMixRequest(string Name, string? Description, Guid BowlId, string Strength, string TasteProfile, bool IsPublic, Guid? CreatedBy, decimal? Price, IReadOnlyCollection<MixInputItem> Items);
public sealed record RecommendMixRequest(Guid BranchId, string Strength, string TasteProfile, Guid BowlId, bool AvailableOnly);
public sealed record UpdateMixRequest(string? Name, string? Description, Guid? BowlId, string? Strength, string? TasteProfile, decimal? Price, bool? IsPublic, bool? IsActive, IReadOnlyCollection<MixInputItem>? Items);
public sealed record UpdateMixVisibilityRequest(bool IsPublic);
public sealed record CalculatedMix(decimal TotalGrams, decimal Cost, IReadOnlyCollection<CalculatedMixItem> Items);
public sealed record CalculatedMixItem(Guid TobaccoId, decimal Percent, decimal Grams);
public sealed record CalculationResult(string? Error, CalculatedMix? Value);
public sealed record InventoryAvailabilityRequest(Guid BranchId, IReadOnlyCollection<InventoryAvailabilityRequestItem> Items);
public sealed record InventoryAvailabilityRequestItem(Guid TobaccoId, decimal RequiredGrams);
public sealed record InventoryAvailabilityResponse(Guid BranchId, bool IsAvailable, IReadOnlyCollection<InventoryAvailabilityItem> Items);
public sealed record InventoryAvailabilityItem(Guid TobaccoId, decimal RequiredGrams, decimal StockGrams, bool IsAvailable, decimal ShortageGrams);
