using HookahPlatform.Contracts;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace HookahPlatform.BuildingBlocks;

public static class ApiDefaults
{
    public static WebApplicationBuilder AddHookahServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.AddHookahObservability(serviceName);
        builder.Services.AddSingleton(new ServiceInfo(serviceName));
        builder.Services.AddTransient<ServiceAuthenticationHandler>();
        builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter, ServiceAuthenticationHttpMessageHandlerFilter>();
        builder.Services.AddHttpClient();
        builder.AddOutboxPersistence();
        builder.Services.AddSingleton<InMemoryEventStore>();
        builder.Services.AddSingleton<IEventPublisher, EventForwardingPublisher>();
        builder.Services.AddSingleton<IOutboxBrokerPublisher, RabbitMqOutboxPublisher>();
        builder.Services.AddSingleton<OutboxDispatcher>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<OutboxDispatcher>());
        builder.AddRuntimeCache(serviceName);
        builder.Services.AddSingleton<JwtTokenService>();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
        });

        return builder;
    }

    public static WebApplication UseHookahServiceDefaults(this WebApplication app)
    {
        app.UseCors();
        app.UseHookahObservability();
        app.UseServiceAccessControl();

        app.MapGet("/", (ServiceInfo service) => Results.Ok(new
        {
            service = service.Name,
            status = "ready",
            utcNow = DateTimeOffset.UtcNow
        }));

        app.MapGet("/health", (ServiceInfo service, IConfiguration configuration) => Results.Ok(new
        {
            service = service.Name,
            status = "healthy",
            environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
            utcNow = DateTimeOffset.UtcNow
        }));

        app.MapGet("/events/debug", (InMemoryEventStore store) => Results.Ok(store.Events));
        app.MapGet("/outbox/debug", async (OutboxDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.OutboxMessages
                .AsNoTracking()
                .OrderByDescending(message => message.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken)));
        app.MapPost("/outbox/dispatch", async (OutboxDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchBatchAsync(cancellationToken)));

        return app;
    }

    private static void AddRuntimeCache(this WebApplicationBuilder builder, string serviceName)
    {
        if (builder.Configuration.GetValue("Redis:Enabled", false))
        {
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
                options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? $"{serviceName}:";
            });
            return;
        }

        builder.Services.AddDistributedMemoryCache();
    }
}

public sealed record ServiceInfo(string Name);

public static class ServiceAccessControl
{
    public const string UserIdHeader = "X-User-Id";
    public const string UserRoleHeader = "X-User-Role";
    public const string UserPermissionsHeader = "X-User-Permissions";
    public const string GatewaySecretHeader = "X-Gateway-Secret";
    public const string ServiceNameHeader = "X-Service-Name";
    public const string ServiceSecretHeader = "X-Service-Secret";

    public static IApplicationBuilder UseServiceAccessControl(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var service = context.RequestServices.GetRequiredService<ServiceInfo>();
            if (service.Name.Equals("api-gateway", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;

            if (EndpointAccessPolicy.IsPublic(method, path))
            {
                await next();
                return;
            }

            var requiredPermissions = EndpointAccessPolicy.GetRequiredPermissions(method, path);
            var serviceAuthenticated = HasValidServiceContext(context);

            if (EndpointAccessPolicy.IsInternal(path))
            {
                if (serviceAuthenticated)
                {
                    await next();
                    return;
                }

            Observability.AccessDeniedCounter.Add(1, KeyValuePair.Create<string, object?>("reason", "service_auth_required"), KeyValuePair.Create<string, object?>("path", path));
            await WriteAccessDeniedAsync(context, StatusCodes.Status401Unauthorized, "service_auth_required", "Internal endpoint requires service authentication.");
            return;
            }

            if (requiredPermissions is null)
            {
                await next();
                return;
            }

            if (serviceAuthenticated || HasValidGatewayUserContext(context, requiredPermissions))
            {
                await next();
                return;
            }

            Observability.AccessDeniedCounter.Add(1, KeyValuePair.Create<string, object?>("reason", "forbidden"), KeyValuePair.Create<string, object?>("path", path));
            await WriteAccessDeniedAsync(context, StatusCodes.Status403Forbidden, "forbidden", "Missing required service/user authentication or permissions.", requiredPermissions);
        });
    }

    private static bool HasValidServiceContext(HttpContext context)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var expectedSecret = configuration["Security:InternalServiceSecret"];
        if (string.IsNullOrWhiteSpace(expectedSecret)) return false;

        var serviceName = context.Request.Headers[ServiceNameHeader].ToString();
        var serviceSecret = context.Request.Headers[ServiceSecretHeader].ToString();
        return !string.IsNullOrWhiteSpace(serviceName) && FixedTimeEquals(serviceSecret, expectedSecret);
    }

    private static bool HasValidGatewayUserContext(HttpContext context, IReadOnlyCollection<string> requiredPermissions)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var expectedGatewaySecret = configuration["Security:GatewaySecret"];
        if (string.IsNullOrWhiteSpace(expectedGatewaySecret)) return false;
        if (!FixedTimeEquals(context.Request.Headers[GatewaySecretHeader].ToString(), expectedGatewaySecret)) return false;

        if (!Guid.TryParse(context.Request.Headers[UserIdHeader].ToString(), out _)) return false;
        var role = context.Request.Headers[UserRoleHeader].ToString();
        if (string.IsNullOrWhiteSpace(role)) return false;

        if (!EndpointAccessPolicy.HasAnyPermission(role, requiredPermissions)) return false;
        if (requiredPermissions.Count == 0) return true;

        var forwardedPermissions = context.Request.Headers[UserPermissionsHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return forwardedPermissions.Contains("*") || requiredPermissions.Any(forwardedPermissions.Contains);
    }

    private static bool FixedTimeEquals(string actual, string expected)
    {
        var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static Task WriteAccessDeniedAsync(HttpContext context, int statusCode, string code, string message, IReadOnlyCollection<string>? requiredPermissions = null)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { code, message, requiredPermissions });
    }
}

public sealed class ServiceAuthenticationHandler(ServiceInfo serviceInfo, IConfiguration configuration) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var secret = configuration["Security:InternalServiceSecret"];
        if (!serviceInfo.Name.Equals("api-gateway", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(secret))
        {
            request.Headers.Remove(ServiceAccessControl.ServiceNameHeader);
            request.Headers.Remove(ServiceAccessControl.ServiceSecretHeader);
            request.Headers.TryAddWithoutValidation(ServiceAccessControl.ServiceNameHeader, serviceInfo.Name);
            request.Headers.TryAddWithoutValidation(ServiceAccessControl.ServiceSecretHeader, secret);
        }

        var correlationId = System.Diagnostics.Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains(Observability.CorrelationIdHeader))
        {
            request.Headers.TryAddWithoutValidation(Observability.CorrelationIdHeader, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class ServiceAuthenticationHttpMessageHandlerFilter(IServiceProvider serviceProvider) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Insert(0, serviceProvider.GetRequiredService<ServiceAuthenticationHandler>());
        };
    }
}

public static class RuntimeCache
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<T?> GetJsonAsync<T>(this IDistributedCache cache, string key, CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(key, cancellationToken);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Options);
    }

    public static Task SetJsonAsync<T>(this IDistributedCache cache, string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return cache.SetStringAsync(key, JsonSerializer.Serialize(value, Options), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
    }
}

public interface IEventPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
    Task<bool> ForwardAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

public sealed class InMemoryEventStore
{
    private readonly List<IIntegrationEvent> _events = [];

    public IReadOnlyCollection<IIntegrationEvent> Events => _events.AsReadOnly();

    public void Append(IIntegrationEvent integrationEvent)
    {
        _events.Add(integrationEvent);
    }
}

public sealed class EventForwardingPublisher : IEventPublisher
{
    private readonly InMemoryEventStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;

    public EventForwardingPublisher(InMemoryEventStore store, IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await SaveToOutboxAsync(integrationEvent, cancellationToken);
        await ForwardAsync(integrationEvent, cancellationToken);
    }

    public async Task<bool> ForwardAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _store.Append(integrationEvent);

        var client = _httpClientFactory.CreateClient("events");
        var analyticsForwarded = await ForwardToAnalyticsAsync(client, integrationEvent, cancellationToken);
        var notificationsForwarded = await ForwardToNotificationsAsync(client, integrationEvent, cancellationToken);
        var inventoryForwarded = await ForwardToInventoryAsync(client, integrationEvent, cancellationToken);
        return analyticsForwarded && notificationsForwarded && inventoryForwarded;
    }

    private async Task SaveToOutboxAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            db.OutboxMessages.Add(OutboxMessageFactory.Create(integrationEvent));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Publishing must not fail the request while RabbitMQ/outbox infrastructure is being hardened.
        }
    }

    private async Task<bool> ForwardToAnalyticsAsync(HttpClient client, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Services:analytics-service:BaseUrl"] ?? "http://analytics-service:8080";
        var envelope = AnalyticsEventEnvelope.From(integrationEvent);
        if (envelope is null)
        {
            return true;
        }

        return await TryPostAsync(client, $"{baseUrl}/api/analytics/events", envelope, cancellationToken);
    }

    private async Task<bool> ForwardToNotificationsAsync(HttpClient client, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Services:notification-service:BaseUrl"] ?? "http://notification-service:8080";
        var request = NotificationEventRequest.From(integrationEvent);
        if (request is null)
        {
            return true;
        }

        return await TryPostAsync(client, $"{baseUrl}/api/notifications/dispatch-event", request, cancellationToken);
    }

    private async Task<bool> ForwardToInventoryAsync(HttpClient client, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["Services:inventory-service:BaseUrl"] ?? "http://inventory-service:8080";
        var request = InventoryEventRequest.From(integrationEvent);
        if (request is null)
        {
            return true;
        }

        return await TryPostAsync(client, $"{baseUrl}/api/inventory/dispatch-event", request, cancellationToken);
    }

    private static async Task<bool> TryPostAsync(HttpClient client, string url, object payload, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            // Durable delivery is handled by RabbitMQ in production; this local fan-out is best-effort.
            return false;
        }
    }
}

public sealed record AnalyticsEventEnvelope(
    Guid EventId,
    string Event,
    DateTimeOffset OccurredAt,
    Guid? OrderId = null,
    Guid? BookingId = null,
    Guid? BranchId = null,
    Guid? TableId = null,
    Guid? MixId = null,
    Guid? TobaccoId = null,
    Guid? HookahMasterId = null,
    decimal? TotalPrice = null,
    decimal? AmountGrams = null,
    int? Rating = null,
    string? Status = null,
    DateTimeOffset? StartTime = null,
    DateTimeOffset? EndTime = null)
{
    public static AnalyticsEventEnvelope? From(IIntegrationEvent integrationEvent)
    {
        return integrationEvent switch
        {
            BookingCreated e => new(e.EventId, nameof(BookingCreated), e.OccurredAt, BookingId: e.BookingId, BranchId: e.BranchId, TableId: e.TableId, StartTime: e.StartTime, EndTime: e.EndTime),
            BookingConfirmed e => new(e.EventId, nameof(BookingConfirmed), e.OccurredAt, BookingId: e.BookingId),
            BookingCancelled e => new(e.EventId, nameof(BookingCancelled), e.OccurredAt, BookingId: e.BookingId),
            OrderCreated e => new(e.EventId, nameof(OrderCreated), e.OccurredAt, OrderId: e.OrderId, BranchId: e.BranchId, TableId: e.TableId, MixId: e.MixId),
            OrderStatusChanged e => new(e.EventId, nameof(OrderStatusChanged), e.OccurredAt, OrderId: e.OrderId, Status: e.Status),
            InventoryWrittenOff e => new(e.EventId, nameof(InventoryWrittenOff), e.OccurredAt, BranchId: e.BranchId, TobaccoId: e.TobaccoId, AmountGrams: e.AmountGrams),
            ReviewCreated e => new(e.EventId, nameof(ReviewCreated), e.OccurredAt, MixId: e.MixId, Rating: e.Rating),
            _ => null
        };
    }
}

public sealed record NotificationEventRequest(Guid EventId, string Event, IReadOnlyCollection<Guid> UserIds, IReadOnlyDictionary<string, string> Values)
{
    public static NotificationEventRequest? From(IIntegrationEvent integrationEvent)
    {
        return integrationEvent switch
        {
            BookingCreated e => new(e.EventId, nameof(BookingCreated), [], new Dictionary<string, string>
            {
                ["bookingId"] = e.BookingId.ToString(),
                ["tableId"] = e.TableId.ToString(),
                ["startTime"] = e.StartTime.ToString("O")
            }),
            BookingConfirmed e => new(e.EventId, nameof(BookingConfirmed), [], new Dictionary<string, string>
            {
                ["bookingId"] = e.BookingId.ToString()
            }),
            BookingCancelled e => new(e.EventId, nameof(BookingCancelled), [], new Dictionary<string, string>
            {
                ["bookingId"] = e.BookingId.ToString(),
                ["reason"] = e.Reason
            }),
            PaymentSucceeded e => new(e.EventId, nameof(PaymentSucceeded), [], new Dictionary<string, string>
            {
                ["paymentId"] = e.PaymentId.ToString(),
                ["amount"] = e.Amount.ToString("0.##")
            }),
            PaymentRefunded e => new(e.EventId, nameof(PaymentRefunded), [], new Dictionary<string, string>
            {
                ["paymentId"] = e.PaymentId.ToString(),
                ["amount"] = e.Amount.ToString("0.##"),
                ["totalRefunded"] = e.TotalRefunded.ToString("0.##")
            }),
            LowStockDetected e => new(e.EventId, nameof(LowStockDetected), [], new Dictionary<string, string>
            {
                ["branchId"] = e.BranchId.ToString(),
                ["tobaccoId"] = e.TobaccoId.ToString(),
                ["stockGrams"] = e.StockGrams.ToString("0.##")
            }),
            OrderServed e => new(e.EventId, nameof(OrderServed), [], new Dictionary<string, string>
            {
                ["orderId"] = e.OrderId.ToString(),
                ["branchId"] = e.BranchId.ToString()
            }),
            _ => null
        };
    }
}

public sealed record InventoryEventRequest(Guid EventId, string Event, Guid OrderId, Guid BranchId, Guid MixId, Guid BowlId, DateTimeOffset OccurredAt)
{
    public static InventoryEventRequest? From(IIntegrationEvent integrationEvent)
    {
        return integrationEvent switch
        {
            OrderServed e => new(e.EventId, nameof(OrderServed), e.OrderId, e.BranchId, e.MixId, e.BowlId, e.OccurredAt),
            _ => null
        };
    }
}
