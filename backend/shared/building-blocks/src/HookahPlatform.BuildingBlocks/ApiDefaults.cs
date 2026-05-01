using HookahPlatform.Contracts;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace HookahPlatform.BuildingBlocks;

public static class ApiDefaults
{
    public static WebApplicationBuilder AddHookahServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Services.AddSingleton(new ServiceInfo(serviceName));
        builder.Services.AddHttpClient();
        builder.AddOutboxPersistence();
        builder.Services.AddSingleton<InMemoryEventStore>();
        builder.Services.AddSingleton<IEventPublisher, EventForwardingPublisher>();
        builder.Services.AddSingleton<OutboxDispatcher>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<OutboxDispatcher>());
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

        app.MapGet("/", (ServiceInfo service) => Results.Ok(new
        {
            service = service.Name,
            status = "ready",
            utcNow = DateTimeOffset.UtcNow
        }));

        app.MapGet("/health", (ServiceInfo service) => Results.Ok(new
        {
            service = service.Name,
            status = "healthy"
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
}

public sealed record ServiceInfo(string Name);

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
        return analyticsForwarded && notificationsForwarded;
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
