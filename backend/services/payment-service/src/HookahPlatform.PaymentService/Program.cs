using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.PaymentService.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("payment-service");
builder.AddPostgresDbContext<PaymentDbContext>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<PaymentDbContext>("payment-service");

app.MapPost("/api/payments/create", async (CreatePaymentRequest request, HttpContext context, PaymentDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!CanActForClient(context, request.ClientId))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Client payments can only be created for the current user."), statusCode: StatusCodes.Status403Forbidden);
    }

    if (request.Amount <= 0)
    {
        return HttpResults.Validation("Payment amount must be positive.");
    }

    var discount = 0m;
    if (!string.IsNullOrWhiteSpace(request.Promocode))
    {
        var promo = await ValidatePromocodeAsync(request.Promocode, request.ClientId, request.Amount, httpClientFactory, configuration, cancellationToken);
        if (!promo.IsValid)
        {
            return HttpResults.Validation(promo.Message);
        }

        discount = promo.DiscountAmount;
    }

    var payableAmount = Math.Max(0, request.Amount - discount);
    var payment = new PaymentEntity
    {
        Id = Guid.NewGuid(),
        ClientId = request.ClientId,
        OrderId = request.OrderId,
        BookingId = request.BookingId,
        OriginalAmount = request.Amount,
        DiscountAmount = discount,
        PayableAmount = payableAmount,
        RefundedAmount = 0,
        Currency = request.Currency,
        Provider = request.Provider,
        Promocode = request.Promocode,
        ExternalPaymentId = null,
        Status = PaymentStatuses.Pending,
        Type = request.Type,
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.Payments.Add(payment);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        paymentId = payment.Id,
        paymentUrl = $"https://payment-link.local/{payment.Id}",
        amount = payment.PayableAmount,
        discount = payment.DiscountAmount
    });
});

app.MapPost("/api/payments/webhook/yookassa", async (YooKassaWebhook request, PaymentDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var payment = await db.Payments.FirstOrDefaultAsync(candidate => candidate.Id == request.PaymentId, cancellationToken);
    if (payment is null)
    {
        return HttpResults.NotFound("Payment", request.PaymentId);
    }

    payment.Status = request.Succeeded ? PaymentStatuses.Success : PaymentStatuses.Failed;
    payment.ExternalPaymentId = request.ExternalPaymentId;
    var outboxEvents = new List<IIntegrationEvent>();
    if (request.Succeeded)
    {
        outboxEvents.Add(new PaymentSucceeded(payment.Id, payment.BookingId, payment.OrderId, payment.PayableAmount, DateTimeOffset.UtcNow));
        if (payment.BookingId is not null)
        {
            outboxEvents.Add(new BookingPaid(payment.BookingId.Value, payment.Id, payment.OriginalAmount, DateTimeOffset.UtcNow));
        }
    }
    else
    {
        outboxEvents.Add(new PaymentFailed(payment.Id, request.Reason ?? "Provider rejected payment", DateTimeOffset.UtcNow));
    }
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);

    if (request.Succeeded)
    {
        if (!string.IsNullOrWhiteSpace(payment.Promocode))
        {
            await RedeemPromocodeAsync(payment.Promocode, payment.ClientId, payment.OrderId, payment.OriginalAmount, httpClientFactory, configuration, cancellationToken);
        }

        await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);
        if (payment.BookingId is not null)
        {
            await ConfirmBookingDepositAsync(payment.BookingId.Value, payment.Id, payment.OriginalAmount, httpClientFactory, configuration, cancellationToken);
        }
        if (payment.OrderId is not null)
        {
            await MarkOrderPaidAsync(payment.OrderId.Value, payment.Id, payment.PayableAmount, httpClientFactory, configuration, cancellationToken);
        }
    }
    else
    {
        await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);
    }

    return Results.Ok(payment);
});

app.MapGet("/api/payments/{id:guid}", async (Guid id, PaymentDbContext db, CancellationToken cancellationToken) =>
    await db.Payments.AsNoTracking().FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken) is { } payment
        ? Results.Ok(payment)
        : HttpResults.NotFound("Payment", id));

app.MapPost("/api/payments/{id:guid}/refund", async (Guid id, RefundRequest request, PaymentDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var payment = await db.Payments.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (payment is null)
    {
        return HttpResults.NotFound("Payment", id);
    }

    if (payment.Status is not PaymentStatuses.Success and not PaymentStatuses.PartiallyRefunded)
    {
        return HttpResults.Conflict("Only successful payments can be refunded.");
    }

    var refundable = payment.PayableAmount - payment.RefundedAmount;
    if (request.Amount <= 0 || request.Amount > refundable)
    {
        return HttpResults.Validation("Refund amount must be positive and cannot exceed payment amount.");
    }

    var totalRefunded = payment.RefundedAmount + request.Amount;
    var status = totalRefunded == payment.PayableAmount ? PaymentStatuses.Refunded : PaymentStatuses.PartiallyRefunded;
    payment.Status = status;
    payment.RefundedAmount = totalRefunded;
    var refunded = new PaymentRefunded(id, payment.BookingId, payment.OrderId, request.Amount, totalRefunded, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(refunded);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, refunded, outboxMessage, cancellationToken);
    return Results.Ok(new { paymentId = id, status, request.Amount, totalRefunded, request.Reason });
});

app.Run();

static async Task ConfirmBookingDepositAsync(Guid bookingId, Guid paymentId, decimal amount, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var baseUrl = configuration["Services:booking-service:BaseUrl"] ?? "http://booking-service:8080";
    var client = httpClientFactory.CreateClient("booking-service");
    var response = await client.PatchAsJsonAsync(
        $"{baseUrl}/api/bookings/{bookingId}/payment-succeeded",
        new BookingPaymentSucceededRequest(paymentId, amount),
        cancellationToken);

    response.EnsureSuccessStatusCode();
}

static async Task MarkOrderPaidAsync(Guid orderId, Guid paymentId, decimal amount, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var baseUrl = configuration["Services:order-service:BaseUrl"] ?? "http://order-service:8080";
    var client = httpClientFactory.CreateClient("order-service");
    var response = await client.PatchAsJsonAsync(
        $"{baseUrl}/api/orders/{orderId}/payment-succeeded",
        new OrderPaymentSucceededRequest(paymentId, amount),
        cancellationToken);

    response.EnsureSuccessStatusCode();
}

static async Task<PromoValidation> ValidatePromocodeAsync(string code, Guid clientId, decimal orderAmount, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var promoBaseUrl = configuration["Services:promo-service:BaseUrl"] ?? "http://promo-service:8080";
    var client = httpClientFactory.CreateClient("promo-service");
    var response = await client.PostAsJsonAsync(
        $"{promoBaseUrl}/api/promocodes/validate",
        new ValidatePromocodeRequest(code, clientId, orderAmount),
        cancellationToken);

    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<PromoValidation>(cancellationToken))!;
}

static async Task<PromoRedeemResult> RedeemPromocodeAsync(string code, Guid clientId, Guid? orderId, decimal orderAmount, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var promoBaseUrl = configuration["Services:promo-service:BaseUrl"] ?? "http://promo-service:8080";
    var client = httpClientFactory.CreateClient("promo-service");
    var response = await client.PostAsJsonAsync(
        $"{promoBaseUrl}/api/promocodes/redeem",
        new RedeemPromocodeRequest(code, clientId, orderId, orderAmount),
        cancellationToken);

    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<PromoRedeemResult>(cancellationToken))!;
}

static Guid? GetForwardedUserId(HttpContext context)
{
    return Guid.TryParse(context.Request.Headers[ServiceAccessControl.UserIdHeader].ToString(), out var userId)
        ? userId
        : null;
}

static bool IsServiceRequest(HttpContext context)
{
    return !string.IsNullOrWhiteSpace(context.Request.Headers[ServiceAccessControl.ServiceNameHeader].ToString());
}

static bool HasForwardedPermission(HttpContext context, string permission)
{
    var permissions = context.Request.Headers[ServiceAccessControl.UserPermissionsHeader].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return permissions.Contains("*", StringComparer.OrdinalIgnoreCase) ||
           permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

static bool CanActForClient(HttpContext context, Guid clientId)
{
    return IsServiceRequest(context) ||
           HasForwardedPermission(context, PermissionCodes.OrdersManage) ||
           GetForwardedUserId(context) == clientId;
}

public sealed record CreatePaymentRequest(Guid ClientId, Guid? OrderId, Guid? BookingId, decimal Amount, string Currency, string Type, string Provider, string? Promocode);
public sealed record YooKassaWebhook(Guid PaymentId, string ExternalPaymentId, bool Succeeded, string? Reason);
public sealed record RefundRequest(decimal Amount, string Reason);
public sealed record BookingPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record OrderPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record ValidatePromocodeRequest(string Code, Guid ClientId, decimal OrderAmount);
public sealed record PromoValidation(bool IsValid, decimal DiscountAmount, decimal DiscountedTotal, string Message);
public sealed record RedeemPromocodeRequest(string Code, Guid ClientId, Guid? OrderId, decimal OrderAmount);
public sealed record PromoRedeemResult(bool IsRedeemed, Guid RedemptionId, decimal DiscountAmount, decimal DiscountedTotal);
