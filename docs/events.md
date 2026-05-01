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

`IEventPublisher` is the application boundary for event delivery. Domain services now use transactional outbox writes for business events: the service DbContext maps `integration_outbox`, adds the outbox row before `SaveChangesAsync`, and only then calls `ForwardAsync` for best-effort HTTP fan-out to Analytics/Notification service endpoints.

`PublishAsync` remains available for infrastructure-level publishing when a handler does not already use the domain DbContext. `ForwardAsync` does not create another outbox row, so handlers that persist the outbox atomically do not duplicate events.

RabbitMQ delivery should be implemented as an outbox dispatcher that reads pending `integration_outbox` rows, publishes to `hookah.platform.events` with the routing key from `MessagingCatalog`, and sets `processed_at` only after broker confirmation.

The current dispatcher implementation can replay pending outbox rows through the same HTTP fan-out path via `POST /outbox/dispatch`. A background dispatcher is registered but disabled by default; enable it in exactly one service process with `Outbox__Dispatcher__Enabled=true` to avoid competing workers while RabbitMQ publishing is still replaced by local HTTP fan-out.
