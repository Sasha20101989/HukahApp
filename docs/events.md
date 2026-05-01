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
