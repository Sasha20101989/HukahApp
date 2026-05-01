using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HookahPlatform.BuildingBlocks.Persistence;

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IConfiguration configuration,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly int _batchSize = Math.Max(1, configuration.GetValue("Outbox:Dispatcher:BatchSize", 25));
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Outbox:Dispatcher:IntervalSeconds", 15)));
    private readonly bool _enabled = configuration.GetValue("Outbox:Dispatcher:Enabled", false);

    public async Task<OutboxDispatchResult> DispatchBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var messages = await db.OutboxMessages
            .Where(message => message.ProcessedAt == null)
            .OrderBy(message => message.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(cancellationToken);

        var dispatched = 0;
        var failed = 0;

        foreach (var message in messages)
        {
            var integrationEvent = OutboxMessageSerializer.Deserialize(message);
            if (integrationEvent is null)
            {
                failed++;
                message.Error = $"Unknown integration event type '{message.EventName}'.";
                continue;
            }

            var forwarded = await publisher.ForwardAsync(integrationEvent, cancellationToken);
            if (forwarded)
            {
                dispatched++;
                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
            }
            else
            {
                failed++;
                message.Error = "Outbox dispatcher forwarding failed.";
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new OutboxDispatchResult(messages.Count, dispatched, failed);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox dispatcher iteration failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}

public sealed record OutboxDispatchResult(int Scanned, int Dispatched, int Failed);
