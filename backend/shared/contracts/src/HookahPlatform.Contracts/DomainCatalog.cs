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
}
