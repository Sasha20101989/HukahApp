using HookahPlatform.Contracts;
using Microsoft.AspNetCore.Http;

namespace HookahPlatform.BuildingBlocks.Tenancy;

public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenantContext)
    {
        if (httpContext.Request.Headers.TryGetValue(TenantHeaders.TenantId, out var tenantValues))
        {
            var raw = tenantValues.ToString();
            if (!Guid.TryParse(raw, out var tenantId))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    code = "invalid_tenant_id",
                    message = $"Header {TenantHeaders.TenantId} must be a GUID."
                });
                return;
            }

            var slug = httpContext.Request.Headers.TryGetValue(TenantHeaders.TenantSlug, out var slugValues)
                ? slugValues.ToString()
                : null;
            tenantContext.Set(tenantId, slug);

            System.Diagnostics.Activity.Current?.SetTag("tenant.id", tenantId.ToString());
            if (!string.IsNullOrWhiteSpace(slug)) System.Diagnostics.Activity.Current?.SetTag("tenant.slug", slug);
        }
        else
        {
            // Tenant-aware services will enforce presence once tenant routing is enabled in the gateway.
            if (httpContext.Request.Headers.ContainsKey(TenantHeaders.TenantSlug))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    code = "tenant_id_required",
                    message = $"Header {TenantHeaders.TenantId} is required when {TenantHeaders.TenantSlug} is provided."
                });
                return;
            }
        }

        await _next(httpContext);
    }
}

