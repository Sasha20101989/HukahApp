namespace HookahPlatform.Contracts;

public static class MessagingCatalog
{
    public const string Exchange = "hookah.platform.events";
    public const string DeadLetterExchange = "hookah.platform.dead-letter";

    public static readonly EventRoute[] EventRoutes =
    [
        new(nameof(UserRegistered), "user.registered"),
        new(nameof(BookingCreated), "booking.created"),
        new(nameof(BookingPaid), "booking.paid"),
        new(nameof(BookingConfirmed), "booking.confirmed"),
        new(nameof(BookingCancelled), "booking.cancelled"),
        new(nameof(OrderCreated), "order.created"),
        new(nameof(OrderStatusChanged), "order.status.changed"),
        new(nameof(OrderServed), "order.served"),
        new(nameof(InventoryWrittenOff), "inventory.written-off"),
        new(nameof(LowStockDetected), "inventory.low-stock-detected"),
        new(nameof(PaymentSucceeded), "payment.succeeded"),
        new(nameof(PaymentFailed), "payment.failed"),
        new(nameof(ReviewCreated), "review.created"),
        new(nameof(MixCreated), "mix.created")
    ];
}

public sealed record EventRoute(string EventName, string RoutingKey);
