namespace HookahPlatform.Contracts;

public static class OrderStatuses
{
    public const string New = "NEW";
    public const string Accepted = "ACCEPTED";
    public const string Preparing = "PREPARING";
    public const string Ready = "READY";
    public const string Served = "SERVED";
    public const string Smoking = "SMOKING";
    public const string CoalChangeRequired = "COAL_CHANGE_REQUIRED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
}

public static class BookingStatuses
{
    public const string New = "NEW";
    public const string WaitingPayment = "WAITING_PAYMENT";
    public const string Paid = "PAID";
    public const string Confirmed = "CONFIRMED";
    public const string ClientArrived = "CLIENT_ARRIVED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string NoShow = "NO_SHOW";
}

public static class PaymentStatuses
{
    public const string Pending = "PENDING";
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
    public const string Refunded = "REFUNDED";
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
}

public static class HookahStatuses
{
    public const string Available = "AVAILABLE";
    public const string InUse = "IN_USE";
    public const string Washing = "WASHING";
    public const string Broken = "BROKEN";
    public const string WrittenOff = "WRITTEN_OFF";
}

public static class RoleCodes
{
    public const string Owner = "OWNER";
    public const string Manager = "MANAGER";
    public const string HookahMaster = "HOOKAH_MASTER";
    public const string Waiter = "WAITER";
    public const string Client = "CLIENT";
}

public static class PermissionCodes
{
    public const string BranchesManage = "branches.manage";
    public const string StaffManage = "staff.manage";
    public const string MixesManage = "mixes.manage";
    public const string InventoryManage = "inventory.manage";
    public const string OrdersManage = "orders.manage";
    public const string BookingsManage = "bookings.manage";
    public const string AnalyticsRead = "analytics.read";
    public const string BookingsCreate = "bookings.create";
    public const string TenantsManage = "tenants.manage";
}

public static class RolePermissionCatalog
{
    public static readonly PermissionDefinition[] Permissions =
    [
        new(PermissionCodes.BranchesManage, "Manage branches, halls, zones and tables"),
        new(PermissionCodes.StaffManage, "Manage staff accounts and shifts"),
        new(PermissionCodes.MixesManage, "Manage bowls, tobaccos and mixes"),
        new(PermissionCodes.InventoryManage, "Manage stock and inventory movements"),
        new(PermissionCodes.OrdersManage, "Manage hookah orders and order statuses"),
        new(PermissionCodes.BookingsManage, "Manage bookings and no-show statuses"),
        new(PermissionCodes.AnalyticsRead, "Read analytics dashboards and reports"),
        new(PermissionCodes.BookingsCreate, "Create client bookings"),
        new(PermissionCodes.TenantsManage, "Manage SaaS tenants and tenant settings")
    ];

    public static readonly RoleDefinition[] Roles =
    [
        new("Owner", RoleCodes.Owner, ["*"]),
        new("Manager", RoleCodes.Manager, [
            PermissionCodes.BranchesManage,
            PermissionCodes.StaffManage,
            PermissionCodes.MixesManage,
            PermissionCodes.InventoryManage,
            PermissionCodes.OrdersManage,
            PermissionCodes.BookingsManage,
            PermissionCodes.AnalyticsRead
        ]),
        new("Hookah master", RoleCodes.HookahMaster, [
            PermissionCodes.MixesManage,
            PermissionCodes.InventoryManage,
            PermissionCodes.OrdersManage
        ]),
        new("Waiter", RoleCodes.Waiter, [
            PermissionCodes.OrdersManage,
            PermissionCodes.BookingsManage
        ]),
        new("Client", RoleCodes.Client, [
            PermissionCodes.BookingsCreate
        ])
    ];

    public static IReadOnlyCollection<string> GetPermissions(string role)
    {
        var definition = Roles.FirstOrDefault(candidate =>
            candidate.Code.Equals(role, StringComparison.OrdinalIgnoreCase) ||
            candidate.Name.Equals(role, StringComparison.OrdinalIgnoreCase) ||
            candidate.Code.Replace("_", string.Empty).Equals(role, StringComparison.OrdinalIgnoreCase));

        return definition?.Permissions ?? [];
    }
}

public sealed record RoleDefinition(string Name, string Code, IReadOnlyCollection<string> Permissions);
public sealed record PermissionDefinition(string Code, string Description);
