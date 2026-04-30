namespace HookahPlatform.Contracts;

public static class ServiceCatalog
{
    public static readonly ServiceDescriptor[] Services =
    [
        new("auth-service", "/api/auth", "Registration, login, JWT and refresh sessions"),
        new("user-service", "/api/users", "Clients, staff profiles, roles and branch assignments"),
        new("branch-service", "/api/branches", "Branches, halls, tables and hookah devices"),
        new("mixology-service", "/api/mixes", "Bowls, tobaccos, mixes, grams calculation and recommendations"),
        new("inventory-service", "/api/inventory", "Branch stock, movements, adjustments and low-stock checks"),
        new("order-service", "/api/orders", "Hookah orders, statuses, assignments and coal timers"),
        new("booking-service", "/api/bookings", "Availability, reservations, deposits and no-shows"),
        new("payment-service", "/api/payments", "Deposits, order payments, provider webhooks and refunds"),
        new("notification-service", "/api/notifications", "CRM, Telegram, SMS, email and push notifications"),
        new("analytics-service", "/api/analytics", "Revenue, mix popularity, usage and staff performance"),
        new("review-service", "/api/reviews", "Client reviews for orders and mixes"),
        new("promo-service", "/api/promocodes", "Promo code creation and validation")
    ];
}

public sealed record ServiceDescriptor(string Code, string BasePath, string Responsibility);
