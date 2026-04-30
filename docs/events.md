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
- `ReviewCreated`
- `MixCreated`

`IEventPublisher` is the application boundary for event delivery. The current local implementation records events for diagnostics; RabbitMQ delivery should be added behind the same interface so service code does not depend on transport details.
