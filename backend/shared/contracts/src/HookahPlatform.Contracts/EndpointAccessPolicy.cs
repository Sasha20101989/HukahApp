namespace HookahPlatform.Contracts;

public static class EndpointAccessPolicy
{
    private static readonly EndpointPermissionRule[] Rules =
    [
        new("GET", "/api/users/me", []),
        new("GET", "/api/users/", []),
        new("GET", "/api/users", [PermissionCodes.StaffManage, PermissionCodes.BookingsManage, PermissionCodes.OrdersManage]),
        new("POST", "/api/users/clients", [PermissionCodes.BookingsCreate, PermissionCodes.StaffManage]),
        new("POST", "/api/users/staff", [PermissionCodes.StaffManage]),
        new("PATCH", "/api/users", [PermissionCodes.BookingsCreate, PermissionCodes.StaffManage]),
        new("DELETE", "/api/users", [PermissionCodes.StaffManage]),
        new("GET", "/api/staff", [PermissionCodes.StaffManage]),
        new("POST", "/api/staff", [PermissionCodes.StaffManage]),
        new("PATCH", "/api/staff", [PermissionCodes.StaffManage]),
        new("GET", "/api/roles", [PermissionCodes.StaffManage]),
        new("GET", "/api/permissions", [PermissionCodes.StaffManage]),
        new("PATCH", "/api/roles", [PermissionCodes.StaffManage]),

        new("POST", "/api/branches", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/branches", [PermissionCodes.BranchesManage]),
        new("PUT", "/api/branches", [PermissionCodes.BranchesManage]),
        new("POST", "/api/zones", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/zones", [PermissionCodes.BranchesManage]),
        new("DELETE", "/api/zones", [PermissionCodes.BranchesManage]),
        new("POST", "/api/halls", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/halls", [PermissionCodes.BranchesManage]),
        new("DELETE", "/api/halls", [PermissionCodes.BranchesManage]),
        new("POST", "/api/tables", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/tables", [PermissionCodes.BranchesManage]),
        new("DELETE", "/api/tables", [PermissionCodes.BranchesManage]),
        new("GET", "/api/tables", [PermissionCodes.BranchesManage]),
        new("POST", "/api/hookahs", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/hookahs", [PermissionCodes.BranchesManage]),
        new("DELETE", "/api/hookahs", [PermissionCodes.BranchesManage]),
        new("GET", "/api/hookahs", [PermissionCodes.BranchesManage]),

        new("GET", "/api/bowls", [PermissionCodes.MixesManage]),
        new("POST", "/api/bowls", [PermissionCodes.MixesManage]),
        new("PATCH", "/api/bowls", [PermissionCodes.MixesManage]),
        new("DELETE", "/api/bowls", [PermissionCodes.MixesManage]),
        new("GET", "/api/tobaccos", [PermissionCodes.MixesManage]),
        new("POST", "/api/tobaccos", [PermissionCodes.MixesManage]),
        new("PATCH", "/api/tobaccos", [PermissionCodes.MixesManage]),
        new("DELETE", "/api/tobaccos", [PermissionCodes.MixesManage]),
        new("POST", "/api/mixes/calculate", [PermissionCodes.BookingsCreate, PermissionCodes.MixesManage]),
        new("POST", "/api/mixes/recommend", [PermissionCodes.BookingsCreate, PermissionCodes.MixesManage]),
        new("POST", "/api/mixes", [PermissionCodes.MixesManage]),
        new("PATCH", "/api/mixes", [PermissionCodes.MixesManage]),
        new("DELETE", "/api/mixes", [PermissionCodes.MixesManage]),

        new("GET", "/api/inventory", [PermissionCodes.InventoryManage]),
        new("POST", "/api/inventory", [PermissionCodes.InventoryManage]),
        new("PATCH", "/api/inventory", [PermissionCodes.InventoryManage]),
        new("GET", "/api/orders", [PermissionCodes.OrdersManage]),
        new("POST", "/api/orders", [PermissionCodes.OrdersManage]),
        new("PATCH", "/api/orders", [PermissionCodes.OrdersManage]),
        new("DELETE", "/api/orders", [PermissionCodes.OrdersManage]),

        new("GET", "/api/bookings", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("POST", "/api/bookings/holds", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("DELETE", "/api/bookings/holds", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("POST", "/api/bookings", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("PATCH", "/api/bookings", [PermissionCodes.BookingsManage]),
        new("DELETE", "/api/bookings", [PermissionCodes.BookingsManage]),
        new("GET", "/api/payments", [PermissionCodes.OrdersManage]),
        new("POST", "/api/payments/create", [PermissionCodes.BookingsCreate, PermissionCodes.OrdersManage]),
        new("POST", "/api/payments", [PermissionCodes.OrdersManage]),

        new("GET", "/api/notifications", [PermissionCodes.BookingsManage]),
        new("GET", "/api/notifications/templates", [PermissionCodes.BookingsManage]),
        new("POST", "/api/notifications/templates", [PermissionCodes.BookingsManage]),
        new("PUT", "/api/notifications/templates", [PermissionCodes.BookingsManage]),
        new("DELETE", "/api/notifications/templates", [PermissionCodes.BookingsManage]),
        new("POST", "/api/notifications/send", [PermissionCodes.BookingsManage]),
        new("PATCH", "/api/notifications", [PermissionCodes.BookingsManage]),
        new("DELETE", "/api/notifications", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("GET", "/api/notifications/preferences", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("PUT", "/api/notifications/preferences", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("GET", "/api/analytics", [PermissionCodes.AnalyticsRead]),
        new("POST", "/api/analytics", [PermissionCodes.AnalyticsRead]),
        new("POST", "/api/reviews", [PermissionCodes.BookingsCreate, PermissionCodes.OrdersManage]),
        new("PATCH", "/api/reviews", [PermissionCodes.BookingsCreate, PermissionCodes.OrdersManage]),
        new("DELETE", "/api/reviews", [PermissionCodes.BookingsCreate, PermissionCodes.OrdersManage]),
        new("GET", "/api/promocodes", [PermissionCodes.OrdersManage]),
        new("POST", "/api/promocodes", [PermissionCodes.OrdersManage]),
        new("PATCH", "/api/promocodes", [PermissionCodes.OrdersManage]),
        new("DELETE", "/api/promocodes", [PermissionCodes.OrdersManage])
    ];

    private static readonly string[] PublicAllMethodPrefixes =
    [
        "/api/auth",
        "/api/catalog",
        "/api/payments/webhook",
        "/api/promocodes/validate"
    ];

    private static readonly string[] PublicExactPaths = ["/", "/health", "/persistence/health"];

    private static readonly string[] PublicReadPrefixes =
    [
        "/api/branches",
        "/api/mixes",
        "/api/reviews",
        "/api/bookings/availability",
        "/api/payments/status"
    ];

    private static readonly string[] InternalPrefixes =
    [
        "/events/debug",
        "/outbox",
        "/api/analytics/events",
        "/api/notifications/dispatch-event",
        "/api/inventory/dispatch-event"
    ];

    public static bool IsPublic(string method, string path)
    {
        return PublicExactPaths.Any(candidate => path.Equals(candidate, StringComparison.OrdinalIgnoreCase)) ||
               PublicAllMethodPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
               IsReadMethod(method) && PublicReadPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsInternal(string path)
    {
        return InternalPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyCollection<string>? GetRequiredPermissions(string method, string path)
    {
        if (IsPublic(method, path) || IsInternal(path)) return null;

        var matched = Rules
            .Where(rule => method.Equals(rule.Method, StringComparison.OrdinalIgnoreCase) && path.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rule => rule.PathPrefix.Length)
            .FirstOrDefault();

        if (matched is not null) return matched.RequiredPermissions;
        if (IsReadMethod(method)) return [];
        return [PermissionCodes.StaffManage];
    }

    public static bool HasAnyPermission(string role, IReadOnlyCollection<string> requiredPermissions)
    {
        if (requiredPermissions.Count == 0) return true;

        var permissions = RolePermissionCatalog.GetPermissions(role);
        return permissions.Contains("*") || requiredPermissions.Any(permissions.Contains);
    }

    private static bool IsReadMethod(string method)
    {
        return method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
               method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record EndpointPermissionRule(string Method, string PathPrefix, IReadOnlyCollection<string> RequiredPermissions);
