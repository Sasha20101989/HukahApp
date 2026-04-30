# Integration Events

Events are defined in `backend/shared/contracts/src/HookahPlatform.Contracts/DomainEvents.cs`.

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
- `ReviewCreated`
- `MixCreated`

The current publisher is in-memory for local smoke testing. Replace `IEventPublisher` with a RabbitMQ implementation when durable messaging is added.
