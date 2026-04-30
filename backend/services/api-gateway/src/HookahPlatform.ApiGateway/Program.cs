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

    var requiredPermissions = GatewayAccessPolicy.GetRequiredPermissions(method, requestPath);
    if (requiredPermissions is not null)
    {
        if (principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { code = "authorization_required", message = "Bearer token is required for this endpoint." }, cancellationToken);
            return;
        }

        var permissions = RolePermissionCatalog.GetPermissions(principal.Role);
        var hasPermission = permissions.Contains("*") || requiredPermissions.Any(permissions.Contains);
        if (!hasPermission)
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
        upstreamRequest.Headers.Remove("X-User-Id");
        upstreamRequest.Headers.Remove("X-User-Role");
        upstreamRequest.Headers.Remove("X-User-Permissions");
        var permissions = RolePermissionCatalog.GetPermissions(principal.Role);
        upstreamRequest.Headers.Add("X-User-Id", principal.UserId.ToString());
        upstreamRequest.Headers.Add("X-User-Role", principal.Role);
        upstreamRequest.Headers.Add("X-User-Permissions", string.Join(",", permissions));
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

public static class GatewayAccessPolicy
{
    private static readonly EndpointPermissionRule[] Rules =
    [
        new("POST", "/api/users/staff", [PermissionCodes.StaffManage]),
        new("PATCH", "/api/users", [PermissionCodes.StaffManage]),
        new("DELETE", "/api/users", [PermissionCodes.StaffManage]),
        new("POST", "/api/staff", [PermissionCodes.StaffManage]),
        new("PATCH", "/api/staff", [PermissionCodes.StaffManage]),

        new("POST", "/api/branches", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/branches", [PermissionCodes.BranchesManage]),
        new("POST", "/api/zones", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/zones", [PermissionCodes.BranchesManage]),
        new("POST", "/api/halls", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/halls", [PermissionCodes.BranchesManage]),
        new("POST", "/api/tables", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/tables", [PermissionCodes.BranchesManage]),
        new("POST", "/api/hookahs", [PermissionCodes.BranchesManage]),
        new("PATCH", "/api/hookahs", [PermissionCodes.BranchesManage]),

        new("POST", "/api/bowls", [PermissionCodes.MixesManage]),
        new("PATCH", "/api/bowls", [PermissionCodes.MixesManage]),
        new("DELETE", "/api/bowls", [PermissionCodes.MixesManage]),
        new("POST", "/api/tobaccos", [PermissionCodes.MixesManage]),
        new("PATCH", "/api/tobaccos", [PermissionCodes.MixesManage]),
        new("DELETE", "/api/tobaccos", [PermissionCodes.MixesManage]),
        new("POST", "/api/mixes", [PermissionCodes.MixesManage]),
        new("PATCH", "/api/mixes", [PermissionCodes.MixesManage]),
        new("DELETE", "/api/mixes", [PermissionCodes.MixesManage]),

        new("POST", "/api/inventory", [PermissionCodes.InventoryManage]),
        new("PATCH", "/api/inventory", [PermissionCodes.InventoryManage]),
        new("POST", "/api/orders", [PermissionCodes.OrdersManage]),
        new("PATCH", "/api/orders", [PermissionCodes.OrdersManage]),
        new("DELETE", "/api/orders", [PermissionCodes.OrdersManage]),

        new("POST", "/api/bookings", [PermissionCodes.BookingsCreate, PermissionCodes.BookingsManage]),
        new("PATCH", "/api/bookings", [PermissionCodes.BookingsManage]),
        new("DELETE", "/api/bookings", [PermissionCodes.BookingsManage]),
        new("POST", "/api/payments/create", [PermissionCodes.BookingsCreate, PermissionCodes.OrdersManage]),
        new("POST", "/api/payments", [PermissionCodes.OrdersManage]),

        new("POST", "/api/notifications/send", [PermissionCodes.BookingsManage]),
        new("POST", "/api/reviews", [PermissionCodes.BookingsCreate, PermissionCodes.OrdersManage]),
        new("POST", "/api/promocodes", [PermissionCodes.OrdersManage]),
        new("PATCH", "/api/promocodes", [PermissionCodes.OrdersManage])
    ];

    private static readonly string[] PublicPrefixes =
    [
        "/",
        "/health",
        "/events/debug",
        "/api/catalog",
        "/api/auth",
        "/api/mixes/calculate",
        "/api/mixes/recommend",
        "/api/payments/webhook",
        "/api/promocodes/validate"
    ];

    public static IReadOnlyCollection<string>? GetRequiredPermissions(string method, string path)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return null;
        }

        if (PublicPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix != "/"))
        {
            return null;
        }

        return Rules
            .Where(rule => method.Equals(rule.Method, StringComparison.OrdinalIgnoreCase) &&
                           path.StartsWith(rule.PathPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rule => rule.PathPrefix.Length)
            .FirstOrDefault()?.RequiredPermissions ?? [PermissionCodes.StaffManage];
    }
}

public sealed record EndpointPermissionRule(string Method, string PathPrefix, IReadOnlyCollection<string> RequiredPermissions);
