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

Consumers:

- `inventory.order-served` is consumed by Inventory Service.
- `analytics.events` is consumed by Analytics Service.
- `notification.events` is consumed by Notification Service.

Enable consumers with `RabbitMQ__Consumers__Enabled=true`.

Consumers use manual acknowledgements. Invalid messages are routed to the configured dead-letter exchange, while valid but unsupported events are acknowledged and skipped by the service that does not need them.
