using HookahPlatform.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;

namespace HookahPlatform.BuildingBlocks.Persistence;

public interface IOutboxBrokerPublisher
{
    Task<bool> PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

public sealed class RabbitMqOutboxPublisher(IConfiguration configuration, ILogger<RabbitMqOutboxPublisher> logger) : IOutboxBrokerPublisher
{
    private readonly bool _enabled = configuration.GetValue("RabbitMQ:Enabled", false);
    private readonly string _exchange = configuration["RabbitMQ:Exchange"] ?? MessagingCatalog.Exchange;

    public async Task<bool> PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return false;
        }

        try
        {
            var factory = CreateConnectionFactory();
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            await channel.ExchangeDeclareAsync(
                exchange: _exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = message.EventId.ToString("N"),
                Type = message.EventName,
                Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["event_id"] = message.EventId.ToString("N"),
                    ["event_name"] = message.EventName,
                    ["routing_key"] = message.RoutingKey
                }
            };

            await channel.BasicPublishAsync(
                exchange: _exchange,
                routingKey: message.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "RabbitMQ outbox publish failed for event {EventName} {EventId}.", message.EventName, message.EventId);
            return false;
        }
    }

    private ConnectionFactory CreateConnectionFactory()
    {
        var uri = configuration["RabbitMQ:Uri"];
        if (!string.IsNullOrWhiteSpace(uri))
        {
            return new ConnectionFactory
            {
                Uri = new Uri(uri),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = configuration["RabbitMQ:ClientProvidedName"] ?? "hookah-platform"
            };
        }

        return new ConnectionFactory
        {
            HostName = configuration["RabbitMQ:HostName"] ?? "localhost",
            Port = configuration.GetValue("RabbitMQ:Port", 5672),
            UserName = configuration["RabbitMQ:UserName"] ?? ConnectionFactory.DefaultUser,
            Password = configuration["RabbitMQ:Password"] ?? ConnectionFactory.DefaultPass,
            VirtualHost = configuration["RabbitMQ:VirtualHost"] ?? ConnectionFactory.DefaultVHost,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = configuration["RabbitMQ:ClientProvidedName"] ?? "hookah-platform"
        };
    }
}
