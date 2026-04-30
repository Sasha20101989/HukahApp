using HookahPlatform.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("promo-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var promocodes = new Dictionary<string, Promocode>(StringComparer.OrdinalIgnoreCase);
var redemptions = new List<PromocodeRedemption>();
SeedPromocodes(promocodes);

app.MapGet("/api/promocodes", (bool? activeOnly) =>
{
    var query = promocodes.Values.AsEnumerable();
    if (activeOnly == true)
    {
        query = query.Where(promocode => promocode.IsActive);
    }

    return Results.Ok(query.OrderBy(promocode => promocode.Code));
});

app.MapPost("/api/promocodes", (CreatePromocodeRequest request) =>
{
    if (request.ValidTo < request.ValidFrom)
    {
        return HttpResults.Validation("validTo must be greater than or equal to validFrom.");
    }

    var promocode = new Promocode(
        Guid.NewGuid(),
        request.Code.ToUpperInvariant(),
        request.DiscountType,
        request.DiscountValue,
        request.ValidFrom,
        request.ValidTo,
        request.MaxRedemptions,
        request.PerClientLimit,
        true);
    promocodes[promocode.Code] = promocode;
    return Results.Created($"/api/promocodes/{promocode.Code}", promocode);
});

app.MapPost("/api/promocodes/validate", (ValidatePromocodeRequest request) =>
{
    return Results.Ok(ValidatePromocode(request, promocodes, redemptions));
});

app.MapPost("/api/promocodes/redeem", (RedeemPromocodeRequest request) =>
{
    var validation = ValidatePromocode(new ValidatePromocodeRequest(request.Code, request.ClientId, request.OrderAmount), promocodes, redemptions);
    if (!validation.IsValid)
    {
        return Results.Ok(validation);
    }

    var redemption = new PromocodeRedemption(
        Guid.NewGuid(),
        request.Code.ToUpperInvariant(),
        request.ClientId,
        request.OrderId,
        request.OrderAmount,
        validation.DiscountAmount,
        DateTimeOffset.UtcNow);
    redemptions.Add(redemption);

    return Results.Ok(new PromoRedeemResult(true, redemption.Id, validation.DiscountAmount, validation.DiscountedTotal));
});

app.MapPatch("/api/promocodes/{code}/deactivate", (string code) =>
{
    if (!promocodes.TryGetValue(code, out var promocode))
    {
        return HttpResults.NotFound("Promocode", Guid.Empty);
    }

    promocodes[promocode.Code] = promocode with { IsActive = false };
    return Results.Ok(promocodes[promocode.Code]);
});

app.Run();

static PromoValidation ValidatePromocode(ValidatePromocodeRequest request, IReadOnlyDictionary<string, Promocode> promocodes, IReadOnlyCollection<PromocodeRedemption> redemptions)
{
    if (!promocodes.TryGetValue(request.Code, out var promocode) || !promocode.IsActive)
    {
        return new PromoValidation(false, 0, request.OrderAmount, "Promocode was not found or inactive.");
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (today < promocode.ValidFrom || today > promocode.ValidTo)
    {
        return new PromoValidation(false, 0, request.OrderAmount, "Promocode is outside its validity period.");
    }

    var totalRedemptions = redemptions.Count(redemption => redemption.Code.Equals(promocode.Code, StringComparison.OrdinalIgnoreCase));
    if (promocode.MaxRedemptions is not null && totalRedemptions >= promocode.MaxRedemptions)
    {
        return new PromoValidation(false, 0, request.OrderAmount, "Promocode redemption limit has been reached.");
    }

    var clientRedemptions = redemptions.Count(redemption => redemption.Code.Equals(promocode.Code, StringComparison.OrdinalIgnoreCase) && redemption.ClientId == request.ClientId);
    if (clientRedemptions >= promocode.PerClientLimit)
    {
        return new PromoValidation(false, 0, request.OrderAmount, "Client has already used this promocode.");
    }

    var discount = promocode.DiscountType.Equals("PERCENT", StringComparison.OrdinalIgnoreCase)
        ? Math.Round(request.OrderAmount * promocode.DiscountValue / 100m, 2)
        : promocode.DiscountValue;

    discount = Math.Min(discount, request.OrderAmount);
    return new PromoValidation(true, discount, request.OrderAmount - discount, "Promocode is valid.");
}

static void SeedPromocodes(IDictionary<string, Promocode> promocodes)
{
    var promocode = new Promocode(
        Guid.Parse("a0000000-0000-0000-0000-000000000001"),
        "HOOKAH20",
        "PERCENT",
        20,
        new DateOnly(2026, 5, 1),
        new DateOnly(2026, 6, 1),
        500,
        1,
        true);

    promocodes[promocode.Code] = promocode;
}

public sealed record Promocode(Guid Id, string Code, string DiscountType, decimal DiscountValue, DateOnly ValidFrom, DateOnly ValidTo, int? MaxRedemptions, int PerClientLimit, bool IsActive);
public sealed record PromocodeRedemption(Guid Id, string Code, Guid ClientId, Guid? OrderId, decimal OrderAmount, decimal DiscountAmount, DateTimeOffset CreatedAt);
public sealed record CreatePromocodeRequest(string Code, string DiscountType, decimal DiscountValue, DateOnly ValidFrom, DateOnly ValidTo, int? MaxRedemptions, int PerClientLimit);
public sealed record ValidatePromocodeRequest(string Code, Guid ClientId, decimal OrderAmount);
public sealed record RedeemPromocodeRequest(string Code, Guid ClientId, Guid? OrderId, decimal OrderAmount);
public sealed record PromoValidation(bool IsValid, decimal DiscountAmount, decimal DiscountedTotal, string Message);
public sealed record PromoRedeemResult(bool IsRedeemed, Guid RedemptionId, decimal DiscountAmount, decimal DiscountedTotal);
