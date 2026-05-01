# Integration Events

Events are defined in `backend/shared/contracts/src/HookahPlatform.Contracts/DomainEvents.cs`.

RabbitMQ routing metadata is defined in `backend/shared/contracts/src/HookahPlatform.Contracts/MessagingCatalog.cs`.

Implemented event contracts:

- `UserRegistered`
- `BookingCreated`
- `BookingPaid`
- `BookingConfirmed`
- `BookingCancelled`
- `OrderCreated`
- `OrderStatusChanged`
- `OrderServed`
- `InventoryWrittenOff`
- `LowStockDetected`
- `PaymentSucceeded`
- `PaymentFailed`
- `PaymentRefunded`
- `ReviewCreated`
- `MixCreated`

`IEventPublisher` is the application boundary for immediate local fan-out. Domain services use transactional outbox writes for business events: the service DbContext maps `integration_outbox`, adds the outbox row before `SaveChangesAsync`, and then may call `ForwardAsync` for best-effort HTTP fan-out to Analytics/Notification service endpoints.

`PublishAsync` remains available for infrastructure-level publishing when a handler does not already use the domain DbContext. `ForwardAsync` does not create another outbox row, so handlers that persist the outbox atomically do not duplicate events.

RabbitMQ delivery is implemented as an outbox dispatcher that reads pending `integration_outbox` rows, publishes to `hookah.platform.events` with the routing key from `MessagingCatalog`, and sets `processed_at` only after RabbitMQ publisher confirmation.

If RabbitMQ publishing fails or `RabbitMQ__Enabled=false`, the dispatcher falls back to the same HTTP fan-out path. `POST /outbox/dispatch` can replay pending rows manually. A background dispatcher is registered but disabled by default; enable it in exactly one service process with `Outbox__Dispatcher__Enabled=true` to avoid competing workers.

Analytics and Notification consumers receive `eventId` in fan-out payloads and persist handled ids in `processed_integration_events`. This makes retries safe: duplicate deliveries return success without re-applying counters or creating duplicate CRM notifications.

RabbitMQ consumers are implemented for:

- `inventory-service`: consumes `inventory.order-served` bound to `order.served` and writes off mix tobacco idempotently.
- `analytics-service`: consumes `analytics.events` bound to `#`.
- `notification-service`: consumes `notification.events` bound to `booking.*`, `payment.*`, `inventory.low-stock-detected` and `order.served`.

Both consumers use manual ack/nack and the same idempotent handlers as the HTTP fallback endpoints. Invalid messages are rejected to the dead-letter exchange; valid events that are not relevant to a specific consumer are acknowledged and skipped.

`OrderServed` is the inventory write-off trigger. Order Service emits the event when an order reaches `SERVED`; Inventory Service fetches the mix composition, writes off each tobacco item, emits `InventoryWrittenOff`/`LowStockDetected`, and records the processed event id to make retries safe.
