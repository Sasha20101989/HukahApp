using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("payment-service");
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();

var payments = new Dictionary<Guid, Payment>();

app.MapPost("/api/payments/create", async (CreatePaymentRequest request, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
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
    var payment = new Payment(Guid.NewGuid(), request.ClientId, request.OrderId, request.BookingId, request.Amount, discount, payableAmount, request.Currency, request.Provider, request.Promocode, null, PaymentStatuses.Pending, request.Type, DateTimeOffset.UtcNow);
    payments[payment.Id] = payment;

    return Results.Ok(new
    {
        paymentId = payment.Id,
        paymentUrl = $"https://payment-link.local/{payment.Id}",
        amount = payment.PayableAmount,
        discount = payment.DiscountAmount
    });
});

app.MapPost("/api/payments/webhook/yookassa", async (YooKassaWebhook request, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!payments.TryGetValue(request.PaymentId, out var payment))
    {
        return HttpResults.NotFound("Payment", request.PaymentId);
    }

    var status = request.Succeeded ? PaymentStatuses.Success : PaymentStatuses.Failed;
    var updated = payment with { Status = status, ExternalPaymentId = request.ExternalPaymentId };
    payments[payment.Id] = updated;

    if (request.Succeeded)
    {
        if (!string.IsNullOrWhiteSpace(payment.Promocode))
        {
            await RedeemPromocodeAsync(payment.Promocode, payment.ClientId, payment.OrderId, payment.OriginalAmount, httpClientFactory, configuration, cancellationToken);
        }

        await events.PublishAsync(new PaymentSucceeded(payment.Id, payment.BookingId, payment.OrderId, payment.PayableAmount, DateTimeOffset.UtcNow));
        if (payment.BookingId is not null)
        {
            await events.PublishAsync(new BookingPaid(payment.BookingId.Value, payment.Id, payment.OriginalAmount, DateTimeOffset.UtcNow));
            await ConfirmBookingDepositAsync(payment.BookingId.Value, payment.Id, payment.OriginalAmount, httpClientFactory, configuration, cancellationToken);
        }
        if (payment.OrderId is not null)
        {
            await MarkOrderPaidAsync(payment.OrderId.Value, payment.Id, payment.PayableAmount, httpClientFactory, configuration, cancellationToken);
        }
    }
    else
    {
        await events.PublishAsync(new PaymentFailed(payment.Id, request.Reason ?? "Provider rejected payment", DateTimeOffset.UtcNow));
    }

    return Results.Ok(updated);
});

app.MapGet("/api/payments/{id:guid}", (Guid id) =>
    payments.TryGetValue(id, out var payment) ? Results.Ok(payment) : HttpResults.NotFound("Payment", id));

app.MapPost("/api/payments/{id:guid}/refund", (Guid id, RefundRequest request) =>
{
    if (!payments.TryGetValue(id, out var payment))
    {
        return HttpResults.NotFound("Payment", id);
    }

    if (payment.Status is not PaymentStatuses.Success and not PaymentStatuses.PartiallyRefunded)
    {
        return HttpResults.Conflict("Only successful payments can be refunded.");
    }

    if (request.Amount <= 0 || request.Amount > payment.PayableAmount)
    {
        return HttpResults.Validation("Refund amount must be positive and cannot exceed payment amount.");
    }

    var status = request.Amount == payment.PayableAmount ? PaymentStatuses.Refunded : PaymentStatuses.PartiallyRefunded;
    payments[id] = payment with { Status = status };
    return Results.Ok(new { paymentId = id, status, request.Amount, request.Reason });
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

public sealed record Payment(Guid Id, Guid ClientId, Guid? OrderId, Guid? BookingId, decimal OriginalAmount, decimal DiscountAmount, decimal PayableAmount, string Currency, string Provider, string? Promocode, string? ExternalPaymentId, string Status, string Type, DateTimeOffset CreatedAt);
public sealed record CreatePaymentRequest(Guid ClientId, Guid? OrderId, Guid? BookingId, decimal Amount, string Currency, string Type, string Provider, string? Promocode);
public sealed record YooKassaWebhook(Guid PaymentId, string ExternalPaymentId, bool Succeeded, string? Reason);
public sealed record RefundRequest(decimal Amount, string Reason);
public sealed record BookingPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record OrderPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record ValidatePromocodeRequest(string Code, Guid ClientId, decimal OrderAmount);
public sealed record PromoValidation(bool IsValid, decimal DiscountAmount, decimal DiscountedTotal, string Message);
public sealed record RedeemPromocodeRequest(string Code, Guid ClientId, Guid? OrderId, decimal OrderAmount);
public sealed record PromoRedeemResult(bool IsRedeemed, Guid RedemptionId, decimal DiscountAmount, decimal DiscountedTotal);
