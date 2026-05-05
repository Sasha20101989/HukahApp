namespace HookahPlatform.Contracts;

public static class ServiceCatalog
{
    public static readonly ServiceDescriptor[] Services =
    [
        new("tenant-service", "/api/tenants", "SaaS tenant registry and tenant settings"),
        new("tenant-service", "/api/audit-logs", "Tenant-scoped audit log read model"),
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

    public static readonly ServiceRoute[] Routes =
    [
        new("/api/tenants", "tenant-service"),
        new("/api/audit-logs", "tenant-service"),
        new("/api/public/tenant", "tenant-service"),
        new("/api/auth", "auth-service"),
        new("/api/permissions", "user-service"),
        new("/api/roles", "user-service"),
        new("/api/users", "user-service"),
        new("/api/staff", "user-service"),
        new("/api/branches", "branch-service"),
        new("/api/zones", "branch-service"),
        new("/api/halls", "branch-service"),
        new("/api/tables", "branch-service"),
        new("/api/hookahs", "branch-service"),
        new("/api/bowls", "mixology-service"),
        new("/api/tobaccos", "mixology-service"),
        new("/api/mixes", "mixology-service"),
        new("/api/inventory", "inventory-service"),
        new("/api/orders", "order-service"),
        new("/api/bookings", "booking-service"),
        new("/api/payments", "payment-service"),
        new("/api/notifications", "notification-service"),
        new("/api/analytics", "analytics-service"),
        new("/api/reviews", "review-service"),
        new("/api/promocodes", "promo-service")
    ];
}

public sealed record ServiceDescriptor(string Code, string BasePath, string Responsibility);
public sealed record ServiceRoute(string PathPrefix, string ServiceCode);
