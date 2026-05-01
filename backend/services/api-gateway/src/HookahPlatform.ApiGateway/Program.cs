using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.Contracts;
using System.Net.Http.Headers;

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

app.Map("/{**path}", async (HttpContext context, IHttpClientFactory httpClientFactory, JwtTokenService jwtTokens, CancellationToken cancellationToken) =>
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
    if (requiredPermissions is not null)
    {
        if (principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { code = "authorization_required", message = "Bearer token is required for this endpoint." }, cancellationToken);
            return;
        }

        if (!EndpointAccessPolicy.HasAnyPermission(principal.Role, requiredPermissions))
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
        var permissions = RolePermissionCatalog.GetPermissions(principal.Role);
        upstreamRequest.Headers.Add(ServiceAccessControl.UserIdHeader, principal.UserId.ToString());
        upstreamRequest.Headers.Add(ServiceAccessControl.UserRoleHeader, principal.Role);
        upstreamRequest.Headers.Add(ServiceAccessControl.UserPermissionsHeader, string.Join(",", permissions));
        upstreamRequest.Headers.Add(ServiceAccessControl.GatewaySecretHeader, builder.Configuration["Security:GatewaySecret"] ?? "local-gateway-secret-change-me");
    }

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

static void StripSecurityForwardingHeaders(HttpRequestMessage request)
{
    var securityHeaders = new[]
    {
        ServiceAccessControl.UserIdHeader,
        ServiceAccessControl.UserRoleHeader,
        ServiceAccessControl.UserPermissionsHeader,
        ServiceAccessControl.GatewaySecretHeader,
        ServiceAccessControl.ServiceNameHeader,
        ServiceAccessControl.ServiceSecretHeader
    };

    foreach (var header in securityHeaders)
    {
        request.Headers.Remove(header);
    }
}
