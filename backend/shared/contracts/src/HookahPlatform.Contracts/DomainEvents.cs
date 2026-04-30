namespace HookahPlatform.Contracts;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record IntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

public sealed record UserRegistered(
    Guid UserId,
    string Phone,
    string Role,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record BookingCreated(
    Guid BookingId,
    Guid BranchId,
    Guid TableId,
    Guid ClientId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record BookingPaid(
    Guid BookingId,
    Guid PaymentId,
    decimal Amount,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record BookingConfirmed(
    Guid BookingId,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record BookingCancelled(
    Guid BookingId,
    string Reason,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record OrderCreated(
    Guid OrderId,
    Guid BranchId,
    Guid TableId,
    Guid? ClientId,
    Guid MixId,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record OrderStatusChanged(
    Guid OrderId,
    string Status,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record OrderServed(
    Guid OrderId,
    Guid BranchId,
    Guid MixId,
    Guid BowlId,
    DateTimeOffset ServedAt,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record InventoryWrittenOff(
    Guid BranchId,
    Guid TobaccoId,
    Guid? OrderId,
    decimal AmountGrams,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record LowStockDetected(
    Guid BranchId,
    Guid TobaccoId,
    decimal StockGrams,
    decimal MinStockGrams,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record PaymentSucceeded(
    Guid PaymentId,
    Guid? BookingId,
    Guid? OrderId,
    decimal Amount,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record PaymentFailed(
    Guid PaymentId,
    string Reason,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record PaymentRefunded(
    Guid PaymentId,
    Guid? BookingId,
    Guid? OrderId,
    decimal Amount,
    decimal TotalRefunded,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record ReviewCreated(
    Guid ReviewId,
    Guid ClientId,
    Guid? MixId,
    int Rating,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);

public sealed record MixCreated(
    Guid MixId,
    string Name,
    Guid BowlId,
    DateTimeOffset OccurredAt) : IntegrationEvent(OccurredAt);
