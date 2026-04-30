using HookahPlatform.Contracts;
using HookahPlatform.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HookahPlatform.BuildingBlocks;

public static class ApiDefaults
{
    public static WebApplicationBuilder AddHookahServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Services.AddSingleton(new ServiceInfo(serviceName));
        builder.Services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
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

        app.MapGet("/events/debug", (IEventPublisher publisher) =>
        {
            var inMemory = (InMemoryEventPublisher)publisher;
            return Results.Ok(inMemory.Events);
        });

        return app;
    }
}

public sealed record ServiceInfo(string Name);

public interface IEventPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

public sealed class InMemoryEventPublisher : IEventPublisher
{
    private readonly List<IIntegrationEvent> _events = [];

    public IReadOnlyCollection<IIntegrationEvent> Events => _events.AsReadOnly();

    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
