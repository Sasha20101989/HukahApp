using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.BuildingBlocks.Tenancy;
using HookahPlatform.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using System.Net.Http.Headers;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("api-gateway");
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();

var routes = ServiceCatalog.Routes.ToDictionary(
    route => route.PathPrefix,
    route => builder.Configuration[$"Services:{route.ServiceCode}:BaseUrl"] ?? $"http://{route.ServiceCode}:8080",
    StringComparer.OrdinalIgnoreCase);

app.MapGet("/api/catalog/services", () => Results.Ok(ServiceCatalog.Services));

app.MapGet("/api/catalog/routes", () => Results.Ok(ServiceCatalog.Routes.Select(route => new
{
    route.ServiceCode,
    route.PathPrefix,
    Upstream = $"{routes[route.PathPrefix]}{route.PathPrefix}"
})));

app.Map("/{**path}", async (
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    IDistributedCache cache,
    JwtTokenService jwtTokens,
    CancellationToken cancellationToken) =>
{
    var requestPath = context.Request.Path.Value ?? string.Empty;
    var method = context.Request.Method;
    var route = routes
        .OrderByDescending(candidate => candidate.Key.Length)
        .FirstOrDefault(candidate => requestPath.StartsWith(candidate.Key, StringComparison.OrdinalIgnoreCase));

    if (string.IsNullOrWhiteSpace(route.Key))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { code = "route_not_found", message = $"No upstream route for '{requestPath}'." }, cancellationToken);
        return;
    }

    if (EndpointAccessPolicy.IsInternal(requestPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { code = "internal_endpoint", message = "This endpoint is internal and is not exposed through the gateway." }, cancellationToken);
        return;
    }

    // Tenant routing is resolved at the gateway boundary.
    // For now:
    // - If caller provides `X-Tenant-Id`, forward it.
    // - Otherwise, default to demo tenant for local/dev compatibility.
    var forwardedTenantId = context.Request.Headers.TryGetValue(TenantHeaders.TenantId, out var tenantIdHeader) && !string.IsNullOrWhiteSpace(tenantIdHeader)
        ? tenantIdHeader.ToString()
        : TenantConstants.DemoTenantId.ToString();

    var forwardedTenantSlug = context.Request.Headers.TryGetValue(TenantHeaders.TenantSlug, out var tenantSlugHeader) && !string.IsNullOrWhiteSpace(tenantSlugHeader)
        ? tenantSlugHeader.ToString()
        : TenantConstants.DemoTenantSlug;

    JwtPrincipal? principal = null;
    var authorization = context.Request.Headers.Authorization.ToString();
    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        principal = jwtTokens.Validate(authorization["Bearer ".Length..].Trim());
        if (principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { code = "invalid_token", message = "Access token is invalid or expired." }, cancellationToken);
            return;
        }
    }

    var requiredPermissions = EndpointAccessPolicy.GetRequiredPermissions(method, requestPath);
    IReadOnlyCollection<string>? resolvedPermissions = null;
    if (principal is not null && requiredPermissions is not null)
    {
        var userServiceBaseUrl = builder.Configuration["Services:user-service:BaseUrl"] ?? "http://user-service:8080";
        var internalServiceSecret = builder.Configuration["Security:InternalServiceSecret"];
        resolvedPermissions = await ResolvePermissionsForRequestAsync(
            httpClientFactory,
            cache,
            userServiceBaseUrl,
            internalServiceSecret,
            forwardedTenantId,
            forwardedTenantSlug,
            principal.UserId,
            cancellationToken);

        if (resolvedPermissions is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "authz_unavailable",
                message = "Authorization service is unavailable. Please retry."
            }, cancellationToken);
            return;
        }
    }

    if (requiredPermissions is not null)
    {
        if (principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { code = "authorization_required", message = "Bearer token is required for this endpoint." }, cancellationToken);
            return;
        }

        // Strict: do not fail open if we cannot resolve permissions from user-service.
        if (resolvedPermissions is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "authz_unavailable",
                message = "Authorization service is unavailable. Please retry."
            }, cancellationToken);
            return;
        }

        if (requiredPermissions.Count > 0 &&
            !resolvedPermissions.Contains("*", StringComparer.OrdinalIgnoreCase) &&
            !requiredPermissions.Any(rp => resolvedPermissions.Contains(rp, StringComparer.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "forbidden",
                message = "User role does not have required permissions.",
                requiredPermissions
            }, cancellationToken);
            return;
        }
    }

    var upstream = new UriBuilder(route.Value)
    {
        Path = requestPath,
        Query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value![1..] : string.Empty
    }.Uri;

    using var upstreamRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), upstream);
    foreach (var header in context.Request.Headers)
    {
        if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
        {
            upstreamRequest.Content ??= new StreamContent(context.Request.Body);
            upstreamRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    StripSecurityForwardingHeaders(upstreamRequest);

    if (context.Request.ContentLength > 0 && upstreamRequest.Content is null)
    {
        upstreamRequest.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
        {
            upstreamRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
        }
    }

    if (principal is not null)
    {
        var permissions = resolvedPermissions ?? Array.Empty<string>();
        upstreamRequest.Headers.Add(ServiceAccessControl.UserIdHeader, principal.UserId.ToString());
        upstreamRequest.Headers.Add(ServiceAccessControl.UserRoleHeader, principal.Role);
        upstreamRequest.Headers.Add(ServiceAccessControl.UserPermissionsHeader, string.Join(",", permissions));
        upstreamRequest.Headers.Add(ServiceAccessControl.GatewaySecretHeader, builder.Configuration["Security:GatewaySecret"] ?? "local-gateway-secret-change-me");
    }

    upstreamRequest.Headers.Remove(TenantHeaders.TenantId);
    upstreamRequest.Headers.Remove(TenantHeaders.TenantSlug);
    upstreamRequest.Headers.TryAddWithoutValidation(TenantHeaders.TenantId, forwardedTenantId);
    upstreamRequest.Headers.TryAddWithoutValidation(TenantHeaders.TenantSlug, forwardedTenantSlug);

    var client = httpClientFactory.CreateClient("gateway");
    using var upstreamResponse = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
    foreach (var header in upstreamResponse.Headers)
    {
        context.Response.Headers[header.Key] = header.Value.ToArray();
    }
    foreach (var header in upstreamResponse.Content.Headers)
    {
        context.Response.Headers[header.Key] = header.Value.ToArray();
    }
    context.Response.Headers.Remove("transfer-encoding");

    await upstreamResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
});

app.Run();

static async Task<IReadOnlyCollection<string>?> ResolvePermissionsForRequestAsync(
    IHttpClientFactory httpClientFactory,
    IDistributedCache cache,
    string userServiceBaseUrl,
    string? internalServiceSecret,
    string forwardedTenantId,
    string forwardedTenantSlug,
    Guid userId,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(internalServiceSecret))
    {
        return null;
    }

    var cacheKey = $"t:{forwardedTenantId}:authz:user:{userId}:perms";
    var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
    if (!string.IsNullOrWhiteSpace(cached))
    {
        return cached
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    var upstream = new UriBuilder(userServiceBaseUrl)
    {
        Path = $"/api/users/{userId}/permissions"
    }.Uri;

    using var request = new HttpRequestMessage(HttpMethod.Get, upstream);
    request.Headers.TryAddWithoutValidation(TenantHeaders.TenantId, forwardedTenantId);
    request.Headers.TryAddWithoutValidation(TenantHeaders.TenantSlug, forwardedTenantSlug);
    request.Headers.TryAddWithoutValidation(ServiceAccessControl.ServiceNameHeader, "api-gateway");
    request.Headers.TryAddWithoutValidation(ServiceAccessControl.ServiceSecretHeader, internalServiceSecret);

    var client = httpClientFactory.CreateClient("gateway-authz");
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        return null;
    }

    try
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!root.TryGetProperty("userId", out var userIdProp)) return null;
        var userIdText = userIdProp.ValueKind == JsonValueKind.String ? userIdProp.GetString() : userIdProp.ToString();
        if (!Guid.TryParse(userIdText, out var parsedUserId) || parsedUserId == Guid.Empty) return null;
        if (parsedUserId != userId) return null;

        if (!root.TryGetProperty("permissions", out var permissionsProp) || permissionsProp.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var permissions = permissionsProp
            .EnumerateArray()
            .Select(p => p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await cache.SetStringAsync(
            cacheKey,
            string.Join(",", permissions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) },
            cancellationToken);

        return permissions;
    }
    catch
    {
        return null;
    }
}

static void StripSecurityForwardingHeaders(HttpRequestMessage request)
{
    var securityHeaders = new[]
    {
        ServiceAccessControl.UserIdHeader,
        ServiceAccessControl.UserRoleHeader,
        ServiceAccessControl.UserPermissionsHeader,
        ServiceAccessControl.GatewaySecretHeader,
        ServiceAccessControl.ServiceNameHeader,
        ServiceAccessControl.ServiceSecretHeader,
        TenantHeaders.TenantId,
        TenantHeaders.TenantSlug
    };

    foreach (var header in securityHeaders)
    {
        request.Headers.Remove(header);
    }
}
