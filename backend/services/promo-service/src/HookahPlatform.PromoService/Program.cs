using HookahPlatform.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("promo-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var promocodes = new Dictionary<string, Promocode>(StringComparer.OrdinalIgnoreCase);

app.MapPost("/api/promocodes", (CreatePromocodeRequest request) =>
{
    if (request.ValidTo < request.ValidFrom)
    {
        return HttpResults.Validation("validTo must be greater than or equal to validFrom.");
    }

    var promocode = new Promocode(Guid.NewGuid(), request.Code.ToUpperInvariant(), request.DiscountType, request.DiscountValue, request.ValidFrom, request.ValidTo, true);
    promocodes[promocode.Code] = promocode;
    return Results.Created($"/api/promocodes/{promocode.Code}", promocode);
});

app.MapPost("/api/promocodes/validate", (ValidatePromocodeRequest request) =>
{
    if (!promocodes.TryGetValue(request.Code, out var promocode) || !promocode.IsActive)
    {
        return Results.Ok(new PromoValidation(false, 0, "Promocode was not found or inactive."));
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (today < promocode.ValidFrom || today > promocode.ValidTo)
    {
        return Results.Ok(new PromoValidation(false, 0, "Promocode is outside its validity period."));
    }

    var discount = promocode.DiscountType.Equals("PERCENT", StringComparison.OrdinalIgnoreCase)
        ? Math.Round(request.OrderAmount * promocode.DiscountValue / 100m, 2)
        : promocode.DiscountValue;

    discount = Math.Min(discount, request.OrderAmount);
    return Results.Ok(new PromoValidation(true, discount, "Promocode is valid."));
});

app.Run();

public sealed record Promocode(Guid Id, string Code, string DiscountType, decimal DiscountValue, DateOnly ValidFrom, DateOnly ValidTo, bool IsActive);
public sealed record CreatePromocodeRequest(string Code, string DiscountType, decimal DiscountValue, DateOnly ValidFrom, DateOnly ValidTo);
public sealed record ValidatePromocodeRequest(string Code, Guid ClientId, decimal OrderAmount);
public sealed record PromoValidation(bool IsValid, decimal DiscountAmount, string Message);
