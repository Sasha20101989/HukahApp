namespace HookahPlatform.BuildingBlocks.Tenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? TenantSlug { get; }
    bool HasTenant { get; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public string? TenantSlug { get; private set; }
    public bool HasTenant => TenantId.HasValue;

    public void Set(Guid tenantId, string? tenantSlug)
    {
        TenantId = tenantId;
        TenantSlug = string.IsNullOrWhiteSpace(tenantSlug) ? null : tenantSlug.Trim();
    }
}

