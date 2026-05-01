# RabbitMQ

RabbitMQ is included in `infrastructure/docker-compose.yml`.

Outbox delivery is implemented in `HookahPlatform.BuildingBlocks` through `RabbitMqOutboxPublisher`.

Configuration:

- `RabbitMQ__Enabled=true`
- `RabbitMQ__HostName=rabbitmq`
- `RabbitMQ__Port=5672`
- `RabbitMQ__UserName=guest`
- `RabbitMQ__Password=guest`
- `RabbitMQ__Exchange=hookah.platform.events`

The background outbox dispatcher should be enabled in exactly one service process with `Outbox__Dispatcher__Enabled=true`.
