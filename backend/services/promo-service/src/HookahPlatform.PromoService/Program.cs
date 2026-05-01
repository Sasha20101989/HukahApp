using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.PromoService.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("promo-service");
builder.AddPostgresDbContext<PromoDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<PromoDbContext>("promo-service");

app.MapGet("/api/promocodes", async (bool? activeOnly, PromoDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Promocodes.AsNoTracking();
    if (activeOnly == true)
    {
        query = query.Where(promocode => promocode.IsActive);
    }

    return Results.Ok(await query.OrderBy(promocode => promocode.Code).ToListAsync(cancellationToken));
});

app.MapPost("/api/promocodes", async (CreatePromocodeRequest request, PromoDbContext db, CancellationToken cancellationToken) =>
{
    if (request.ValidTo < request.ValidFrom)
    {
        return HttpResults.Validation("validTo must be greater than or equal to validFrom.");
    }

    var code = request.Code.ToUpperInvariant();
    if (await db.Promocodes.AnyAsync(promocode => promocode.Code == code, cancellationToken))
    {
        return HttpResults.Conflict("Promocode already exists.");
    }

    var promocode = new PromocodeEntity
    {
        Id = Guid.NewGuid(),
        Code = code,
        DiscountType = request.DiscountType,
        DiscountValue = request.DiscountValue,
        ValidFrom = request.ValidFrom,
        ValidTo = request.ValidTo,
        MaxRedemptions = request.MaxRedemptions,
        PerClientLimit = request.PerClientLimit,
        IsActive = true
    };
    db.Promocodes.Add(promocode);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/promocodes/{promocode.Code}", promocode);
});

app.MapPost("/api/promocodes/validate", async (ValidatePromocodeRequest request, PromoDbContext db, CancellationToken cancellationToken) =>
{
    return Results.Ok(await ValidatePromocodeAsync(request, db, cancellationToken));
});

app.MapPost("/api/promocodes/redeem", async (RedeemPromocodeRequest request, PromoDbContext db, CancellationToken cancellationToken) =>
{
    var validation = await ValidatePromocodeAsync(new ValidatePromocodeRequest(request.Code, request.ClientId, request.OrderAmount), db, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.Ok(validation);
    }

    var redemption = new PromocodeRedemptionEntity
    {
        Id = Guid.NewGuid(),
        Code = request.Code.ToUpperInvariant(),
        ClientId = request.ClientId,
        OrderId = request.OrderId,
        OrderAmount = request.OrderAmount,
        DiscountAmount = validation.DiscountAmount,
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.Redemptions.Add(redemption);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new PromoRedeemResult(true, redemption.Id, validation.DiscountAmount, validation.DiscountedTotal));
});

app.MapPatch("/api/promocodes/{code}/deactivate", async (string code, PromoDbContext db, CancellationToken cancellationToken) =>
{
    var normalized = code.ToUpperInvariant();
    var promocode = await db.Promocodes.FirstOrDefaultAsync(candidate => candidate.Code == normalized, cancellationToken);
    if (promocode is null)
    {
        return HttpResults.NotFound("Promocode", Guid.Empty);
    }

    promocode.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(promocode);
});

app.Run();

static async Task<PromoValidation> ValidatePromocodeAsync(ValidatePromocodeRequest request, PromoDbContext db, CancellationToken cancellationToken)
{
    var code = request.Code.ToUpperInvariant();
    var promocode = await db.Promocodes.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);
    if (promocode is null || !promocode.IsActive)
    {
        return new PromoValidation(false, 0, request.OrderAmount, "Promocode was not found or inactive.");
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (today < promocode.ValidFrom || today > promocode.ValidTo)
    {
        return new PromoValidation(false, 0, request.OrderAmount, "Promocode is outside its validity period.");
    }

    var totalRedemptions = await db.Redemptions.CountAsync(redemption => redemption.Code == promocode.Code, cancellationToken);
    if (promocode.MaxRedemptions is not null && totalRedemptions >= promocode.MaxRedemptions)
    {
        return new PromoValidation(false, 0, request.OrderAmount, "Promocode redemption limit has been reached.");
    }

    var clientRedemptions = await db.Redemptions.CountAsync(redemption => redemption.Code == promocode.Code && redemption.ClientId == request.ClientId, cancellationToken);
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

public sealed record CreatePromocodeRequest(string Code, string DiscountType, decimal DiscountValue, DateOnly ValidFrom, DateOnly ValidTo, int? MaxRedemptions, int PerClientLimit);
public sealed record ValidatePromocodeRequest(string Code, Guid ClientId, decimal OrderAmount);
public sealed record RedeemPromocodeRequest(string Code, Guid ClientId, Guid? OrderId, decimal OrderAmount);
public sealed record PromoValidation(bool IsValid, decimal DiscountAmount, decimal DiscountedTotal, string Message);
public sealed record PromoRedeemResult(bool IsRedeemed, Guid RedemptionId, decimal DiscountAmount, decimal DiscountedTotal);
