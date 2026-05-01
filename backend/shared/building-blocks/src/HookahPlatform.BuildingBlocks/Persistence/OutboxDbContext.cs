using HookahPlatform.Contracts;
using HookahPlatform.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HookahPlatform.BuildingBlocks.Persistence;

public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
    }
}

public static class OutboxModelBuilderExtensions
{
    public static ModelBuilder ConfigureIntegrationOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("integration_outbox");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.EventId).HasColumnName("event_id");
            entity.Property(message => message.EventName).HasColumnName("event_name");
            entity.Property(message => message.RoutingKey).HasColumnName("routing_key");
            entity.Property(message => message.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(message => message.OccurredAt).HasColumnName("occurred_at");
            entity.Property(message => message.CreatedAt).HasColumnName("created_at");
            entity.Property(message => message.ProcessedAt).HasColumnName("processed_at");
            entity.Property(message => message.Error).HasColumnName("error");
        });

        return modelBuilder;
    }
}

public static class OutboxDbContextExtensions
{
    public static OutboxMessage AddOutboxMessage(this DbContext db, IIntegrationEvent integrationEvent)
    {
        var message = OutboxMessageFactory.Create(integrationEvent);
        db.Set<OutboxMessage>().Add(message);
        return message;
    }

    public static IReadOnlyCollection<OutboxMessage> AddOutboxMessages(this DbContext db, IEnumerable<IIntegrationEvent> integrationEvents)
    {
        var messages = integrationEvents.Select(OutboxMessageFactory.Create).ToArray();
        db.Set<OutboxMessage>().AddRange(messages);
        return messages;
    }

    public static async Task ForwardAndMarkOutboxAsync(this DbContext db, IEventPublisher publisher, IIntegrationEvent integrationEvent, OutboxMessage outboxMessage, CancellationToken cancellationToken)
    {
        var forwarded = await publisher.ForwardAsync(integrationEvent, cancellationToken);
        outboxMessage.Error = forwarded ? null : "Immediate forwarding failed; waiting for outbox dispatcher.";
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task ForwardAndMarkOutboxAsync(this DbContext db, IEventPublisher publisher, IEnumerable<IIntegrationEvent> integrationEvents, IEnumerable<OutboxMessage> outboxMessages, CancellationToken cancellationToken)
    {
        foreach (var pair in integrationEvents.Zip(outboxMessages))
        {
            var forwarded = await publisher.ForwardAsync(pair.First, cancellationToken);
            pair.Second.Error = forwarded ? null : "Immediate forwarding failed; waiting for outbox dispatcher.";
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public static class OutboxMessageFactory
{
    public static OutboxMessage Create(IIntegrationEvent integrationEvent)
    {
        var eventName = integrationEvent.GetType().Name;
        var routingKey = MessagingCatalog.EventRoutes.FirstOrDefault(route => route.EventName == eventName)?.RoutingKey ?? eventName;

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = integrationEvent.EventId,
            EventName = eventName,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            OccurredAt = integrationEvent.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}

public static class OutboxMessageSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, Type> EventTypes = typeof(IIntegrationEvent).Assembly
        .GetTypes()
        .Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type) && !type.IsAbstract && type.Name.EndsWith("Event", StringComparison.Ordinal) == false)
        .ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);

    public static IIntegrationEvent? Deserialize(OutboxMessage message)
    {
        if (!EventTypes.TryGetValue(message.EventName, out var eventType))
        {
            return null;
        }

        return JsonSerializer.Deserialize(message.Payload, eventType, SerializerOptions) as IIntegrationEvent;
    }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
}
