# RabbitMQ

RabbitMQ is included in `infrastructure/docker-compose.yml`.

The current .NET publisher is in-memory. Replace `IEventPublisher` from `HookahPlatform.BuildingBlocks` with a RabbitMQ implementation when durable event delivery is added.
