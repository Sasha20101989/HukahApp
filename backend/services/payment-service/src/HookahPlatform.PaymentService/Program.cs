using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("payment-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var payments = new Dictionary<Guid, Payment>();

app.MapPost("/api/payments/create", (CreatePaymentRequest request) =>
{
    if (request.Amount <= 0)
    {
        return HttpResults.Validation("Payment amount must be positive.");
    }

    var payment = new Payment(Guid.NewGuid(), request.ClientId, request.OrderId, request.BookingId, request.Amount, request.Currency, request.Provider, null, "PENDING", request.Type, DateTimeOffset.UtcNow);
    payments[payment.Id] = payment;

    return Results.Ok(new
    {
        paymentId = payment.Id,
        paymentUrl = $"https://payment-link.local/{payment.Id}"
    });
});

app.MapPost("/api/payments/webhook/yookassa", async (YooKassaWebhook request, IEventPublisher events) =>
{
    if (!payments.TryGetValue(request.PaymentId, out var payment))
    {
        return HttpResults.NotFound("Payment", request.PaymentId);
    }

    var status = request.Succeeded ? "SUCCESS" : "FAILED";
    var updated = payment with { Status = status, ExternalPaymentId = request.ExternalPaymentId };
    payments[payment.Id] = updated;

    if (request.Succeeded)
    {
        await events.PublishAsync(new PaymentSucceeded(payment.Id, payment.BookingId, payment.OrderId, payment.Amount, DateTimeOffset.UtcNow));
        if (payment.BookingId is not null)
        {
            await events.PublishAsync(new BookingPaid(payment.BookingId.Value, payment.Id, payment.Amount, DateTimeOffset.UtcNow));
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

    if (request.Amount <= 0 || request.Amount > payment.Amount)
    {
        return HttpResults.Validation("Refund amount must be positive and cannot exceed payment amount.");
    }

    var status = request.Amount == payment.Amount ? "REFUNDED" : "PARTIALLY_REFUNDED";
    payments[id] = payment with { Status = status };
    return Results.Ok(new { paymentId = id, status, request.Amount, request.Reason });
});

app.Run();

public sealed record Payment(Guid Id, Guid ClientId, Guid? OrderId, Guid? BookingId, decimal Amount, string Currency, string Provider, string? ExternalPaymentId, string Status, string Type, DateTimeOffset CreatedAt);
public sealed record CreatePaymentRequest(Guid ClientId, Guid? OrderId, Guid? BookingId, decimal Amount, string Currency, string Type, string Provider);
public sealed record YooKassaWebhook(Guid PaymentId, string ExternalPaymentId, bool Succeeded, string? Reason);
public sealed record RefundRequest(decimal Amount, string Reason);
