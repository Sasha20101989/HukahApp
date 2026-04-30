using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("mixology-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var bowls = new Dictionary<Guid, Bowl>();
var tobaccos = new Dictionary<Guid, Tobacco>();
var mixes = new Dictionary<Guid, Mix>();
SeedMixology(bowls, tobaccos, mixes);

app.MapGet("/api/bowls", () => Results.Ok(bowls.Values.Where(bowl => bowl.IsActive).OrderBy(bowl => bowl.Name)));

app.MapPost("/api/bowls", (CreateBowlRequest request) =>
{
    var bowl = new Bowl(Guid.NewGuid(), request.Name, request.Type, request.CapacityGrams, request.RecommendedStrength, request.AverageSmokeMinutes, true);
    bowls[bowl.Id] = bowl;
    return Results.Created($"/api/bowls/{bowl.Id}", bowl);
});

app.MapPatch("/api/bowls/{id:guid}", (Guid id, UpdateBowlRequest request) =>
{
    if (!bowls.TryGetValue(id, out var bowl))
    {
        return HttpResults.NotFound("Bowl", id);
    }

    var updated = bowl with
    {
        Name = request.Name ?? bowl.Name,
        Type = request.Type ?? bowl.Type,
        CapacityGrams = request.CapacityGrams ?? bowl.CapacityGrams,
        RecommendedStrength = request.RecommendedStrength ?? bowl.RecommendedStrength,
        AverageSmokeMinutes = request.AverageSmokeMinutes ?? bowl.AverageSmokeMinutes,
        IsActive = request.IsActive ?? bowl.IsActive
    };

    bowls[id] = updated;
    return Results.Ok(updated);
});

app.MapDelete("/api/bowls/{id:guid}", (Guid id) =>
{
    if (!bowls.TryGetValue(id, out var bowl))
    {
        return HttpResults.NotFound("Bowl", id);
    }

    bowls[id] = bowl with { IsActive = false };
    return Results.NoContent();
});

app.MapGet("/api/tobaccos", (string? brand, string? strength, string? category, bool? isActive) =>
{
    var query = tobaccos.Values.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(brand))
    {
        query = query.Where(tobacco => string.Equals(tobacco.Brand, brand, StringComparison.OrdinalIgnoreCase));
    }
    if (!string.IsNullOrWhiteSpace(strength))
    {
        query = query.Where(tobacco => string.Equals(tobacco.Strength, strength, StringComparison.OrdinalIgnoreCase));
    }
    if (!string.IsNullOrWhiteSpace(category))
    {
        query = query.Where(tobacco => string.Equals(tobacco.Category, category, StringComparison.OrdinalIgnoreCase));
    }
    if (isActive is not null)
    {
        query = query.Where(tobacco => tobacco.IsActive == isActive);
    }

    return Results.Ok(query.OrderBy(tobacco => tobacco.Brand).ThenBy(tobacco => tobacco.Flavor));
});

app.MapPost("/api/tobaccos", (CreateTobaccoRequest request) =>
{
    var tobacco = new Tobacco(Guid.NewGuid(), request.Brand, request.Line, request.Flavor, request.Strength, request.Category, request.Description, request.CostPerGram, true, request.PhotoUrl);
    tobaccos[tobacco.Id] = tobacco;
    return Results.Created($"/api/tobaccos/{tobacco.Id}", tobacco);
});

app.MapPatch("/api/tobaccos/{id:guid}", (Guid id, UpdateTobaccoRequest request) =>
{
    if (!tobaccos.TryGetValue(id, out var tobacco))
    {
        return HttpResults.NotFound("Tobacco", id);
    }

    var updated = tobacco with
    {
        Brand = request.Brand ?? tobacco.Brand,
        Line = request.Line ?? tobacco.Line,
        Flavor = request.Flavor ?? tobacco.Flavor,
        Strength = request.Strength ?? tobacco.Strength,
        Category = request.Category ?? tobacco.Category,
        Description = request.Description ?? tobacco.Description,
        CostPerGram = request.CostPerGram ?? tobacco.CostPerGram,
        IsActive = request.IsActive ?? tobacco.IsActive,
        PhotoUrl = request.PhotoUrl ?? tobacco.PhotoUrl
    };

    tobaccos[id] = updated;
    return Results.Ok(updated);
});

app.MapDelete("/api/tobaccos/{id:guid}", (Guid id) =>
{
    if (!tobaccos.TryGetValue(id, out var tobacco))
    {
        return HttpResults.NotFound("Tobacco", id);
    }

    tobaccos[id] = tobacco with { IsActive = false };
    return Results.NoContent();
});

app.MapGet("/api/mixes", (string? strength, string? tasteProfile, Guid? bowlId, bool? availableOnly, Guid? branchId, bool? publicOnly) =>
{
    var query = mixes.Values.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(strength))
    {
        query = query.Where(mix => string.Equals(mix.Strength, strength, StringComparison.OrdinalIgnoreCase));
    }
    if (!string.IsNullOrWhiteSpace(tasteProfile))
    {
        query = query.Where(mix => string.Equals(mix.TasteProfile, tasteProfile, StringComparison.OrdinalIgnoreCase));
    }
    if (bowlId is not null)
    {
        query = query.Where(mix => mix.BowlId == bowlId);
    }
    if (publicOnly == true)
    {
        query = query.Where(mix => mix.IsPublic);
    }
    if (availableOnly == true)
    {
        query = query.Where(mix => mix.IsActive && mix.Items.All(item => tobaccos.TryGetValue(item.TobaccoId, out var tobacco) && tobacco.IsActive));
    }

    return Results.Ok(query.OrderBy(mix => mix.Name));
});

app.MapGet("/api/mixes/{id:guid}", (Guid id) =>
    mixes.TryGetValue(id, out var mix) ? Results.Ok(mix) : HttpResults.NotFound("Mix", id));

app.MapPost("/api/mixes/calculate", (CalculateMixRequest request) =>
{
    var result = CalculateMix(request.BowlId, request.Items, bowls, tobaccos);
    return result.Error is not null ? HttpResults.Validation(result.Error) : Results.Ok(result.Value);
});

app.MapPost("/api/mixes", async (CreateMixRequest request, IEventPublisher events) =>
{
    var result = CalculateMix(request.BowlId, request.Items, bowls, tobaccos);
    if (result.Error is not null || result.Value is null)
    {
        return HttpResults.Validation(result.Error ?? "Mix calculation failed.");
    }

    var price = request.Price ?? Math.Ceiling(result.Value.Cost * 3.2m / 10m) * 10m;
    var mix = new Mix(
        Guid.NewGuid(),
        request.Name,
        request.Description,
        request.BowlId,
        request.Strength,
        request.TasteProfile,
        result.Value.TotalGrams,
        price,
        result.Value.Cost,
        price - result.Value.Cost,
        request.IsPublic,
        true,
        request.CreatedBy,
        DateTimeOffset.UtcNow,
        result.Value.Items.Select(item => new MixItem(Guid.NewGuid(), item.TobaccoId, item.Percent, item.Grams)).ToArray());

    mixes[mix.Id] = mix;
    await events.PublishAsync(new MixCreated(mix.Id, mix.Name, mix.BowlId, DateTimeOffset.UtcNow));

    return Results.Created($"/api/mixes/{mix.Id}", mix);
});

app.MapPost("/api/mixes/recommend", (RecommendMixRequest request) =>
{
    var query = mixes.Values.Where(mix => mix.IsActive);
    query = query.Where(mix => string.Equals(mix.Strength, request.Strength, StringComparison.OrdinalIgnoreCase));
    query = query.Where(mix => string.Equals(mix.TasteProfile, request.TasteProfile, StringComparison.OrdinalIgnoreCase));
    query = query.Where(mix => mix.BowlId == request.BowlId);

    if (request.AvailableOnly)
    {
        query = query.Where(mix => mix.Items.All(item => tobaccos.TryGetValue(item.TobaccoId, out var tobacco) && tobacco.IsActive));
    }

    return Results.Ok(query.OrderByDescending(mix => mix.Margin).Take(10));
});

app.MapPatch("/api/mixes/{id:guid}", (Guid id, UpdateMixRequest request) =>
{
    if (!mixes.TryGetValue(id, out var mix))
    {
        return HttpResults.NotFound("Mix", id);
    }

    var updated = mix with
    {
        Name = request.Name ?? mix.Name,
        Description = request.Description ?? mix.Description,
        Strength = request.Strength ?? mix.Strength,
        TasteProfile = request.TasteProfile ?? mix.TasteProfile,
        Price = request.Price ?? mix.Price,
        Margin = request.Price is null ? mix.Margin : request.Price.Value - mix.Cost
    };
    mixes[id] = updated;
    return Results.Ok(updated);
});

app.MapPatch("/api/mixes/{id:guid}/visibility", (Guid id, UpdateMixVisibilityRequest request) =>
{
    if (!mixes.TryGetValue(id, out var mix))
    {
        return HttpResults.NotFound("Mix", id);
    }

    mixes[id] = mix with { IsPublic = request.IsPublic };
    return Results.Ok(mixes[id]);
});

app.MapDelete("/api/mixes/{id:guid}", (Guid id) =>
{
    if (!mixes.TryGetValue(id, out var mix))
    {
        return HttpResults.NotFound("Mix", id);
    }

    mixes[id] = mix with { IsActive = false };
    return Results.NoContent();
});

app.Run();

static CalculationResult CalculateMix(Guid bowlId, IReadOnlyCollection<MixInputItem> items, IReadOnlyDictionary<Guid, Bowl> bowls, IReadOnlyDictionary<Guid, Tobacco> tobaccos)
{
    if (!bowls.TryGetValue(bowlId, out var bowl) || !bowl.IsActive)
    {
        return new("Bowl must exist and be active.", null);
    }

    if (items.Count == 0)
    {
        return new("Mix must contain at least one tobacco.", null);
    }

    if (!DomainRules.PercentSumIsValid(items.Select(item => item.Percent)))
    {
        return new("Mix item percent sum must be exactly 100.", null);
    }

    var calculatedItems = new List<CalculatedMixItem>();
    decimal cost = 0;

    foreach (var item in items)
    {
        if (!tobaccos.TryGetValue(item.TobaccoId, out var tobacco) || !tobacco.IsActive)
        {
            return new($"Tobacco '{item.TobaccoId}' must exist and be active.", null);
        }

        var grams = DomainRules.CalculateGrams(bowl.CapacityGrams, item.Percent);
        cost += grams * tobacco.CostPerGram;
        calculatedItems.Add(new CalculatedMixItem(item.TobaccoId, item.Percent, grams));
    }

    return new(null, new CalculatedMix(bowl.CapacityGrams, Math.Round(cost, 2), calculatedItems));
}

static void SeedMixology(IDictionary<Guid, Bowl> bowls, IDictionary<Guid, Tobacco> tobaccos, IDictionary<Guid, Mix> mixes)
{
    var bowlId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    var strawberryId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    var mintId = Guid.Parse("60000000-0000-0000-0000-000000000002");
    var blueberryId = Guid.Parse("60000000-0000-0000-0000-000000000003");

    bowls[bowlId] = new Bowl(bowlId, "Oblako Phunnel M", "PHUNNEL", 18, "MEDIUM", 70, true);
    tobaccos[strawberryId] = new Tobacco(strawberryId, "Darkside", "Base", "Strawberry", "STRONG", "BERRY", "Strawberry flavor", 8.5m, true, null);
    tobaccos[mintId] = new Tobacco(mintId, "Musthave", "Classic", "Mint", "MEDIUM", "FRESH", "Cooling mint", 6.8m, true, null);
    tobaccos[blueberryId] = new Tobacco(blueberryId, "Element", "Air", "Blueberry", "LIGHT", "BERRY", "Blueberry flavor", 7.2m, true, null);

    var items = new[]
    {
        new MixItem(Guid.NewGuid(), strawberryId, 40, 7.2m),
        new MixItem(Guid.NewGuid(), mintId, 30, 5.4m),
        new MixItem(Guid.NewGuid(), blueberryId, 30, 5.4m)
    };

    var cost = Math.Round(items[0].Grams * 8.5m + items[1].Grams * 6.8m + items[2].Grams * 7.2m, 2);
    var price = 850m;
    var mixId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    mixes[mixId] = new Mix(mixId, "Berry Ice", "Berry and fresh medium mix", bowlId, "MEDIUM", "BERRY_FRESH", 18, price, cost, price - cost, true, true, null, DateTimeOffset.UtcNow, items);
}

public sealed record Bowl(Guid Id, string Name, string Type, decimal CapacityGrams, string RecommendedStrength, int AverageSmokeMinutes, bool IsActive);
public sealed record Tobacco(Guid Id, string Brand, string Line, string Flavor, string Strength, string Category, string? Description, decimal CostPerGram, bool IsActive, string? PhotoUrl);
public sealed record Mix(Guid Id, string Name, string? Description, Guid BowlId, string Strength, string TasteProfile, decimal TotalGrams, decimal Price, decimal Cost, decimal Margin, bool IsPublic, bool IsActive, Guid? CreatedBy, DateTimeOffset CreatedAt, IReadOnlyCollection<MixItem> Items);
public sealed record MixItem(Guid Id, Guid TobaccoId, decimal Percent, decimal Grams);
public sealed record CreateBowlRequest(string Name, string Type, decimal CapacityGrams, string RecommendedStrength, int AverageSmokeMinutes);
public sealed record UpdateBowlRequest(string? Name, string? Type, decimal? CapacityGrams, string? RecommendedStrength, int? AverageSmokeMinutes, bool? IsActive);
public sealed record CreateTobaccoRequest(string Brand, string Line, string Flavor, string Strength, string Category, string? Description, decimal CostPerGram, string? PhotoUrl);
public sealed record UpdateTobaccoRequest(string? Brand, string? Line, string? Flavor, string? Strength, string? Category, string? Description, decimal? CostPerGram, bool? IsActive, string? PhotoUrl);
public sealed record MixInputItem(Guid TobaccoId, decimal Percent);
public sealed record CalculateMixRequest(Guid BowlId, IReadOnlyCollection<MixInputItem> Items);
public sealed record CreateMixRequest(string Name, string? Description, Guid BowlId, string Strength, string TasteProfile, bool IsPublic, Guid? CreatedBy, decimal? Price, IReadOnlyCollection<MixInputItem> Items);
public sealed record RecommendMixRequest(Guid BranchId, string Strength, string TasteProfile, Guid BowlId, bool AvailableOnly);
public sealed record UpdateMixRequest(string? Name, string? Description, string? Strength, string? TasteProfile, decimal? Price);
public sealed record UpdateMixVisibilityRequest(bool IsPublic);
public sealed record CalculatedMix(decimal TotalGrams, decimal Cost, IReadOnlyCollection<CalculatedMixItem> Items);
public sealed record CalculatedMixItem(Guid TobaccoId, decimal Percent, decimal Grams);
public sealed record CalculationResult(string? Error, CalculatedMix? Value);
