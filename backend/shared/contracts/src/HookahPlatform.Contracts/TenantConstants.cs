namespace HookahPlatform.Contracts;

public static class TenantConstants
{
    // Local/dev default tenant seeded by migrations.
    public static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string DemoTenantSlug = "demo";
}

