using HookahPlatform.Contracts;

namespace HookahPlatform.BuildingBlocks.Tenancy;

public static class TenantContextExtensions
{
    public static Guid GetTenantIdOrDemo(this ITenantContext tenantContext)
    {
        return tenantContext.TenantId ?? TenantConstants.DemoTenantId;
    }
}

