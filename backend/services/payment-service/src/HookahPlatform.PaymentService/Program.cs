using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Auditing;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Tenancy;
using HookahPlatform.PaymentService.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("payment-service");
builder.AddPostgresDbContext<PaymentDbContext>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<PaymentDbContext>("payment-service");

app.MapPost("/api/payments/create", async (CreatePaymentRequest request, HttpContext context, PaymentDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (!CanActForClient(context, request.ClientId))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Client payments can only be created for the current user."), statusCode: StatusCodes.Status403Forbidden);
    }

    if (request.Amount <= 0)
    {
        return HttpResults.Validation("Payment amount must be positive.");
    }

    if (request.ClientId == Guid.Empty)
    {
        return HttpResults.Validation("Client id is required.");
    }

    var targetCount = (request.BookingId is null ? 0 : 1) + (request.OrderId is null ? 0 : 1);
    if (targetCount != 1)
    {
        return HttpResults.Validation("Payment must target exactly one booking or order.");
    }

    var currency = (request.Currency ?? string.Empty).Trim().ToUpperInvariant();
    var provider = (request.Provider ?? string.Empty).Trim().ToUpperInvariant();
    var type = (request.Type ?? string.Empty).Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(type))
    {
        return HttpResults.Validation("Payment currency, provider and type are required.");
    }
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var providerConfig = await db.TenantPaymentProviders.AsNoTracking().FirstOrDefaultAsync(
        candidate => candidate.TenantId == tenantId && candidate.Provider == provider && candidate.IsActive,
        cancellationToken);
    if (providerConfig is null)
    {
        return HttpResults.Conflict($"Payment provider '{provider}' is not configured for tenant.");
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
        TenantId = tenantId,
        ClientId = request.ClientId,
        OrderId = request.OrderId,
        BookingId = request.BookingId,
        OriginalAmount = request.Amount,
        DiscountAmount = discount,
        PayableAmount = payableAmount,
        RefundedAmount = 0,
        Currency = currency,
        Provider = provider,
        Promocode = request.Promocode,
        ExternalPaymentId = null,
        Status = PaymentStatuses.Pending,
        Type = type,
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.Payments.Add(payment);
    await db.SaveChangesAsync(cancellationToken);

    var returnUrl = BuildReturnUrl(request.ReturnUrl, payment);
    var paymentUrl = BuildPaymentUrl(payment, providerConfig, request.ReturnUrl, configuration);
    return Results.Ok(new CreatePaymentResponse(
        payment.Id,
        paymentUrl,
        returnUrl,
        IsProviderRedirectEnabled(configuration) ? "PROVIDER_REDIRECT" : "LOCAL_RETURN",
        payment.Status,
        payment.PayableAmount,
        payment.DiscountAmount));
});

app.MapGet("/api/payments/providers", async (PaymentDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var providers = await db.TenantPaymentProviders.AsNoTracking()
        .Where(provider => provider.TenantId == tenantId)
        .OrderBy(provider => provider.Provider)
        .Select(provider => new TenantPaymentProviderDto(provider.Id, provider.Provider, provider.DisplayName, provider.IsActive, provider.CreatedAt, provider.UpdatedAt))
        .ToListAsync(cancellationToken);
    return Results.Ok(providers);
});

app.MapPut("/api/payments/providers/{provider}", async (string provider, UpsertTenantPaymentProviderRequest request, HttpContext context, PaymentDbContext db, ITenantContext tenantContext, IAuditLogWriter audit, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var providerCode = provider.Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(providerCode)) return HttpResults.Validation("Provider is required.");
    var displayName = (request.DisplayName ?? providerCode).Trim();
    if (string.IsNullOrWhiteSpace(displayName)) return HttpResults.Validation("Display name is required.");
    if (string.IsNullOrWhiteSpace(request.EncryptedCredentials)) return HttpResults.Validation("Encrypted credentials are required.");
    if (string.IsNullOrWhiteSpace(request.WebhookSecret)) return HttpResults.Validation("Webhook secret is required.");

    var now = DateTimeOffset.UtcNow;
    var entity = await db.TenantPaymentProviders.FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Provider == providerCode && candidate.DisplayName == displayName, cancellationToken);
    if (entity is null)
    {
        entity = new TenantPaymentProviderEntity { Id = Guid.NewGuid(), TenantId = tenantId, Provider = providerCode, DisplayName = displayName, CreatedAt = now };
        db.TenantPaymentProviders.Add(entity);
    }
    entity.EncryptedCredentials = request.EncryptedCredentials.Trim();
    entity.WebhookSecretHash = HashSecret(request.WebhookSecret);
    entity.IsActive = request.IsActive;
    entity.UpdatedAt = now;
    await db.SaveChangesAsync(cancellationToken);
    await audit.WriteAsync(tenantId, AuditLogContext.ForwardedUserId(context), "payment.provider.upsert", "tenant_payment_provider", entity.Id.ToString(), "success", AuditLogContext.CorrelationId(context), new { entity.Provider, entity.DisplayName, entity.IsActive }, cancellationToken);
    return Results.Ok(new TenantPaymentProviderDto(entity.Id, entity.Provider, entity.DisplayName, entity.IsActive, entity.CreatedAt, entity.UpdatedAt));
});

app.MapPost("/api/payments/webhook/yookassa", async (YooKassaWebhook request, HttpContext context, PaymentDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (request.PaymentId == Guid.Empty)
    {
        return HttpResults.Validation("Payment id is required.");
    }
    var tenantId = request.TenantId ?? ResolveTenantId(context);
    if (tenantId is null) return HttpResults.Validation("Webhook tenant id is required.");
    var providerConfig = await db.TenantPaymentProviders.AsNoTracking().FirstOrDefaultAsync(
        candidate => candidate.TenantId == tenantId && candidate.Provider == "YOOKASSA" && candidate.IsActive,
        cancellationToken);
    if (providerConfig is null || !IsAuthorizedYooKassaWebhook(context, providerConfig))
    {
        return Results.Json(new ProblemDetailsDto("invalid_webhook_signature", "Payment webhook signature is invalid."), statusCode: StatusCodes.Status401Unauthorized);
    }

    var externalPaymentId = (request.ExternalPaymentId ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(externalPaymentId))
    {
        return HttpResults.Validation("External payment id is required.");
    }

    var externalOwnerId = await db.Payments.AsNoTracking()
        .Where(candidate => candidate.TenantId == tenantId && candidate.ExternalPaymentId == externalPaymentId && candidate.Id != request.PaymentId)
        .Select(candidate => (Guid?)candidate.Id)
        .FirstOrDefaultAsync(cancellationToken);
    if (externalOwnerId is not null)
    {
        return HttpResults.Conflict("External payment id is already linked to another payment.");
    }

    var payment = await db.Payments.FirstOrDefaultAsync(candidate => candidate.Id == request.PaymentId && candidate.TenantId == tenantId, cancellationToken);
    if (payment is null)
    {
        return HttpResults.NotFound("Payment", request.PaymentId);
    }

    var nextStatus = request.Succeeded ? PaymentStatuses.Success : PaymentStatuses.Failed;
    if (IsTerminalPaymentStatus(payment.Status))
    {
        if (payment.Status == nextStatus && string.Equals(payment.ExternalPaymentId, externalPaymentId, StringComparison.Ordinal))
        {
            return Results.Ok(new WebhookPaymentResponse(payment.Id, payment.Status, externalPaymentId, true));
        }

        return HttpResults.Conflict("Terminal payment status cannot be changed by webhook.");
    }

    if (!string.IsNullOrWhiteSpace(payment.ExternalPaymentId) &&
        !string.Equals(payment.ExternalPaymentId, externalPaymentId, StringComparison.Ordinal))
    {
        return HttpResults.Conflict("Payment is already linked to another external payment id.");
    }

    payment.Status = nextStatus;
    payment.ExternalPaymentId = externalPaymentId;
    var outboxEvents = new List<IIntegrationEvent>();
    var occurredAt = DateTimeOffset.UtcNow;
    if (request.Succeeded)
    {
        outboxEvents.Add(new PaymentSucceeded(payment.Id, payment.BookingId, payment.OrderId, payment.PayableAmount, occurredAt));
        if (payment.BookingId is not null)
        {
            outboxEvents.Add(new BookingPaid(payment.BookingId.Value, payment.Id, payment.OriginalAmount, occurredAt));
        }
    }
    else
    {
        outboxEvents.Add(new PaymentFailed(payment.Id, request.Reason ?? "Provider rejected payment", occurredAt));
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

    return Results.Ok(new WebhookPaymentResponse(payment.Id, payment.Status, externalPaymentId, false));
});

app.MapGet("/api/payments", async (Guid? clientId, Guid? orderId, Guid? bookingId, string? status, string? type, HttpContext context, PaymentDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (!IsServiceRequest(context) && !HasForwardedPermission(context, PermissionCodes.OrdersManage))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Payment list is available only to order managers."), statusCode: StatusCodes.Status403Forbidden);
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var query = db.Payments.AsNoTracking().Where(payment => payment.TenantId == tenantId);
    if (clientId is not null) query = query.Where(payment => payment.ClientId == clientId);
    if (orderId is not null) query = query.Where(payment => payment.OrderId == orderId);
    if (bookingId is not null) query = query.Where(payment => payment.BookingId == bookingId);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(payment => payment.Status == status);
    if (!string.IsNullOrWhiteSpace(type)) query = query.Where(payment => payment.Type == type);

    return Results.Ok(await query.OrderByDescending(payment => payment.CreatedAt).Take(200).ToListAsync(cancellationToken));
});

app.MapGet("/api/payments/status/{id:guid}", async (Guid id, PaymentDbContext db, CancellationToken cancellationToken) =>
    await db.Payments.AsNoTracking().Where(payment => payment.Id == id).Select(payment => new PaymentStatusResponse(
        payment.Id,
        payment.BookingId,
        payment.OrderId,
        payment.Status,
        payment.Type,
        payment.Provider,
        payment.PayableAmount,
        payment.RefundedAmount,
        payment.Currency,
        payment.CreatedAt)).FirstOrDefaultAsync(cancellationToken) is { } payment
        ? Results.Ok(payment)
        : HttpResults.NotFound("Payment", id));

app.MapGet("/api/payments/{id:guid}", async (Guid id, PaymentDbContext db, CancellationToken cancellationToken) =>
    await db.Payments.AsNoTracking().FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken) is { } payment
        ? Results.Ok(payment)
        : HttpResults.NotFound("Payment", id));

app.MapPost("/api/payments/{id:guid}/refund", async (Guid id, RefundRequest request, HttpContext context, PaymentDbContext db, IEventPublisher events, ITenantContext tenantContext, IAuditLogWriter audit, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var payment = await db.Payments.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
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

    var reason = request.Reason?.Trim();
    if (string.IsNullOrWhiteSpace(reason)) return HttpResults.Validation("Refund reason is required.");

    var totalRefunded = payment.RefundedAmount + request.Amount;
    var status = totalRefunded == payment.PayableAmount ? PaymentStatuses.Refunded : PaymentStatuses.PartiallyRefunded;
    payment.Status = status;
    payment.RefundedAmount = totalRefunded;
    var refunded = new PaymentRefunded(id, payment.BookingId, payment.OrderId, request.Amount, totalRefunded, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(refunded);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, refunded, outboxMessage, cancellationToken);
    await audit.WriteAsync(AuditLogContext.TenantId(tenantContext), AuditLogContext.ForwardedUserId(context), "payment.refund", "payment", id.ToString(), "success", AuditLogContext.CorrelationId(context), new { request.Amount, totalRefunded, Reason = reason, payment.BookingId, payment.OrderId }, cancellationToken);
    return Results.Ok(new { paymentId = id, status, request.Amount, totalRefunded, Reason = reason });
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

static string BuildPaymentUrl(PaymentEntity payment, TenantPaymentProviderEntity providerConfig, string? returnUrl, IConfiguration configuration)
{
    var checkoutBaseUrl = configuration["Payments:CheckoutBaseUrl"];
    if (!IsProviderRedirectEnabled(configuration))
    {
        return BuildReturnUrl(returnUrl, payment);
    }

    checkoutBaseUrl = checkoutBaseUrl!.Trim();
    var separator = checkoutBaseUrl.Contains('?') ? '&' : '?';
    return $"{checkoutBaseUrl}{separator}paymentId={payment.Id}&tenantId={payment.TenantId}&providerAccount={Uri.EscapeDataString(providerConfig.DisplayName)}&amount={payment.PayableAmount:0.##}&currency={Uri.EscapeDataString(payment.Currency)}&returnUrl={Uri.EscapeDataString(BuildReturnUrl(returnUrl, payment))}";
}

static string BuildReturnUrl(string? returnUrl, PaymentEntity payment)
{
    var baseReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
        ? "http://localhost:3001/payment/return"
        : returnUrl;
    var separator = baseReturnUrl.Contains('?') ? '&' : '?';
    return $"{baseReturnUrl}{separator}paymentId={payment.Id}&bookingId={payment.BookingId}&status=processing";
}

static bool IsProviderRedirectEnabled(IConfiguration configuration)
{
    return !string.IsNullOrWhiteSpace(configuration["Payments:CheckoutBaseUrl"]);
}

static bool IsAuthorizedYooKassaWebhook(HttpContext context, TenantPaymentProviderEntity providerConfig)
{
    var provided = context.Request.Headers["X-Webhook-Secret"].ToString();
    if (string.IsNullOrWhiteSpace(provided)) return false;

    var expectedBytes = Encoding.UTF8.GetBytes(providerConfig.WebhookSecretHash);
    var providedBytes = Encoding.UTF8.GetBytes(HashSecret(provided));
    return expectedBytes.Length == providedBytes.Length &&
           CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
}

static Guid? ResolveTenantId(HttpContext context)
{
    return Guid.TryParse(context.Request.Headers[TenantHeaders.TenantId].ToString(), out var tenantId) ? tenantId : null;
}

static string HashSecret(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes);
}

static bool IsTerminalPaymentStatus(string status)
{
    return status is PaymentStatuses.Success or PaymentStatuses.Failed or PaymentStatuses.Refunded or PaymentStatuses.PartiallyRefunded;
}

public sealed record CreatePaymentRequest(Guid ClientId, Guid? OrderId, Guid? BookingId, decimal Amount, string Currency, string Type, string Provider, string? Promocode, string? ReturnUrl);
public sealed record CreatePaymentResponse(Guid PaymentId, string PaymentUrl, string ReturnUrl, string CheckoutMode, string Status, decimal Amount, decimal Discount);
public sealed record TenantPaymentProviderDto(Guid Id, string Provider, string DisplayName, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record UpsertTenantPaymentProviderRequest(string DisplayName, string EncryptedCredentials, string WebhookSecret, bool IsActive);
public sealed record PaymentStatusResponse(Guid Id, Guid? BookingId, Guid? OrderId, string Status, string Type, string Provider, decimal PayableAmount, decimal RefundedAmount, string Currency, DateTimeOffset CreatedAt);
public sealed record YooKassaWebhook(Guid PaymentId, string ExternalPaymentId, bool Succeeded, string? Reason, Guid? TenantId);
public sealed record WebhookPaymentResponse(Guid PaymentId, string Status, string ExternalPaymentId, bool Duplicate);
public sealed record RefundRequest(decimal Amount, string Reason);
public sealed record BookingPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record OrderPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record ValidatePromocodeRequest(string Code, Guid ClientId, decimal OrderAmount);
public sealed record PromoValidation(bool IsValid, decimal DiscountAmount, decimal DiscountedTotal, string Message);
public sealed record RedeemPromocodeRequest(string Code, Guid ClientId, Guid? OrderId, decimal OrderAmount);
public sealed record PromoRedeemResult(bool IsRedeemed, Guid RedemptionId, decimal DiscountAmount, decimal DiscountedTotal);
