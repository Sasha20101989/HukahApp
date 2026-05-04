namespace HookahPlatform.Contracts;

public static class TenantHeaderNames
{
    public const string TenantId = "X-Tenant-Id";
    public const string TenantSlug = "X-Tenant-Slug";
}

public sealed record TenantDto(
    Guid Id,
    string Slug,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record TenantSettingsDto(
    Guid TenantId,
    string DefaultTimezone,
    string DefaultCurrency,
    bool RequireDeposit);

public sealed record CreateTenantRequest(
    string Slug,
    string Name);

public sealed record UpdateTenantRequest(
    string? Slug,
    string? Name,
    bool? IsActive);

