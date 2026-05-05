using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Auditing;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Tenancy;
using HookahPlatform.Contracts;
using HookahPlatform.TenantService.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("tenant-service");
builder.AddPostgresDbContext<TenantDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<TenantDbContext>("tenant-service");

app.MapGet("/api/public/tenant/branding", async (HttpContext httpContext, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await ResolveRequestedTenantAsync(httpContext, db, cancellationToken);
    if (tenant is null) return HttpResults.NotFound("Tenant", TenantConstants.DemoTenantId);

    return Results.Ok(new TenantBrandingDto(
        tenant.Id,
        tenant.Name,
        LogoUrl: null,
        PrimaryColor: "#1d765f",
        AccentColor: "#b86b20"));
});

app.MapGet("/api/tenants", async (TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenants = await db.Tenants.AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
    return Results.Ok(tenants.Select(x => new TenantDto(x.Id, x.Slug, x.Name, x.IsActive, x.CreatedAt)));
});

app.MapGet("/api/audit-logs", async (string? action, string? targetType, Guid? actorUserId, DateTimeOffset? from, DateTimeOffset? to, int? limit, TenantDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var take = Math.Clamp(limit ?? 100, 1, 500);
    var query = db.AuditLogs.AsNoTracking().Where(log => log.TenantId == tenantId);
    if (!string.IsNullOrWhiteSpace(action)) query = query.Where(log => log.Action == action.Trim());
    if (!string.IsNullOrWhiteSpace(targetType)) query = query.Where(log => log.TargetType == targetType.Trim());
    if (actorUserId is not null) query = query.Where(log => log.ActorUserId == actorUserId);
    if (from is not null) query = query.Where(log => log.CreatedAt >= from);
    if (to is not null) query = query.Where(log => log.CreatedAt <= to);

    var logs = await query.OrderByDescending(log => log.CreatedAt).Take(take).Select(log => new AuditLogDto(
        log.Id,
        log.TenantId,
        log.ActorUserId,
        log.Action,
        log.TargetType,
        log.TargetId,
        log.Result,
        log.CorrelationId,
        log.MetadataJson,
        log.CreatedAt)).ToListAsync(cancellationToken);
    return Results.Ok(logs);
});

app.MapGet("/api/tenants/{id:guid}", async (Guid id, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (tenant is null) return HttpResults.NotFound("Tenant", id);
    return Results.Ok(new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAt));
});

app.MapPost("/api/tenants", async (CreateTenantRequest request, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(slug)) return HttpResults.Validation("Tenant slug is required.");
    if (slug.Length < 2) return HttpResults.Validation("Tenant slug must be at least 2 characters.");

    var name = (request.Name ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(name)) return HttpResults.Validation("Tenant name is required.");

    if (await db.Tenants.AnyAsync(x => x.Slug == slug, cancellationToken))
    {
        return HttpResults.Conflict("Tenant with this slug already exists.");
    }

    var now = DateTimeOffset.UtcNow;
    var tenant = new TenantRecord
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        Name = name,
        IsActive = true,
        CreatedAt = now
    };
    db.Tenants.Add(tenant);
    db.TenantSettings.Add(new TenantSettingsRecord
    {
        TenantId = tenant.Id,
        DefaultTimezone = "Europe/Moscow",
        DefaultCurrency = "RUB",
        RequireDeposit = false
    });

    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/tenants/{tenant.Id}", new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAt));
});

app.MapPatch("/api/tenants/{id:guid}", async (Guid id, UpdateTenantRequest request, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (tenant is null) return HttpResults.NotFound("Tenant", id);

    if (request.Slug is not null)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug)) return HttpResults.Validation("Tenant slug cannot be empty.");
        if (await db.Tenants.AnyAsync(x => x.Slug == slug && x.Id != id, cancellationToken)) return HttpResults.Conflict("Tenant with this slug already exists.");
        tenant.Slug = slug;
    }
    if (request.Name is not null)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return HttpResults.Validation("Tenant name cannot be empty.");
        tenant.Name = name;
    }
    if (request.IsActive is not null) tenant.IsActive = request.IsActive.Value;

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAt));
});

app.MapPatch("/api/tenants/{id:guid}/suspend", async (Guid id, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (tenant is null) return HttpResults.NotFound("Tenant", id);

    tenant.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAt));
});

app.MapPatch("/api/tenants/{id:guid}/reactivate", async (Guid id, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (tenant is null) return HttpResults.NotFound("Tenant", id);

    tenant.IsActive = true;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAt));
});

app.MapPost("/api/tenants/{id:guid}/export", async (Guid id, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (tenant is null) return HttpResults.NotFound("Tenant", id);

    var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == id, cancellationToken);
    return Results.Ok(new TenantExportDto(
        new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.IsActive, tenant.CreatedAt),
        settings is null ? null : new TenantSettingsDto(settings.TenantId, settings.DefaultTimezone, settings.DefaultCurrency, settings.RequireDeposit),
        DateTimeOffset.UtcNow));
});

app.MapDelete("/api/tenants/{id:guid}", async (Guid id, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (tenant is null) return HttpResults.NotFound("Tenant", id);

    // Current schema has is_active only; hard deletion is intentionally avoided until retention/export is finalized.
    tenant.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/tenants/{id:guid}/settings", async (Guid id, TenantDbContext db, CancellationToken cancellationToken) =>
{
    var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == id, cancellationToken);
    if (settings is null) return HttpResults.NotFound("TenantSettings", id);
    return Results.Ok(new TenantSettingsDto(settings.TenantId, settings.DefaultTimezone, settings.DefaultCurrency, settings.RequireDeposit));
});

app.MapPut("/api/tenants/{id:guid}/settings", async (Guid id, TenantSettingsDto request, HttpContext context, TenantDbContext db, IAuditLogWriter audit, CancellationToken cancellationToken) =>
{
    if (id != request.TenantId) return HttpResults.Validation("TenantId mismatch.");
    var settings = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == id, cancellationToken);
    if (settings is null) return HttpResults.NotFound("TenantSettings", id);

    settings.DefaultTimezone = (request.DefaultTimezone ?? "Europe/Moscow").Trim();
    settings.DefaultCurrency = (request.DefaultCurrency ?? "RUB").Trim().ToUpperInvariant();
    settings.RequireDeposit = request.RequireDeposit;

    await db.SaveChangesAsync(cancellationToken);
    await audit.WriteAsync(id, AuditLogContext.ForwardedUserId(context), "tenant.settings.update", "tenant", id.ToString(), "success", AuditLogContext.CorrelationId(context), new { settings.DefaultTimezone, settings.DefaultCurrency, settings.RequireDeposit }, cancellationToken);
    return Results.Ok(new TenantSettingsDto(settings.TenantId, settings.DefaultTimezone, settings.DefaultCurrency, settings.RequireDeposit));
});

app.Run();

static async Task<TenantRecord?> ResolveRequestedTenantAsync(HttpContext httpContext, TenantDbContext db, CancellationToken cancellationToken)
{
    if (httpContext.Request.Headers.TryGetValue(TenantHeaders.TenantId, out var tenantIdHeader) &&
        Guid.TryParse(tenantIdHeader.ToString(), out var tenantId))
    {
        return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId && x.IsActive, cancellationToken);
    }

    if (httpContext.Request.Headers.TryGetValue(TenantHeaders.TenantSlug, out var tenantSlugHeader))
    {
        var slug = tenantSlugHeader.ToString();
        if (!string.IsNullOrWhiteSpace(slug))
        {
            return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive, cancellationToken);
        }
    }

    return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == TenantConstants.DemoTenantId && x.IsActive, cancellationToken);
}
