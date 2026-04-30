using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.AuthService.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.Contracts;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("auth-service");
builder.AddPostgresDbContext<AuthDbContext>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<AuthDbContext>("auth-service");

var roles = RolePermissionCatalog.Roles.ToDictionary(
    role => role.Code,
    role => new Role(Guid.NewGuid(), role.Name, role.Code, role.Permissions.ToArray()),
    StringComparer.OrdinalIgnoreCase);

var users = new Dictionary<Guid, AuthUser>();
var refreshTokens = new Dictionary<string, Guid>();
SeedAuthUsers(users, roles);

app.MapPost("/api/auth/register", async (RegisterRequest request, IEventPublisher events, JwtTokenService tokens, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Password))
    {
        return HttpResults.Validation("Phone and password are required.");
    }

    if (users.Values.Any(user => user.Phone == request.Phone))
    {
        return HttpResults.Conflict("User with this phone already exists.");
    }

    var role = roles[RoleCodes.Client];
    var user = new AuthUser(Guid.NewGuid(), role.Id, request.Name, request.Phone, request.Email, PasswordHasher.Hash(request.Password), "ACTIVE", DateTimeOffset.UtcNow);
    users[user.Id] = user;

    var issuedTokens = IssueTokens(user.Id, role.Code, tokens);
    refreshTokens[issuedTokens.RefreshToken] = user.Id;

    await CreateClientProfileAsync(user, httpClientFactory, configuration, cancellationToken);
    await events.PublishAsync(new UserRegistered(user.Id, user.Phone, role.Code, DateTimeOffset.UtcNow));

    return Results.Ok(issuedTokens);
});

app.MapPost("/api/auth/login", (LoginRequest request, JwtTokenService tokens) =>
{
    var user = users.Values.FirstOrDefault(candidate => candidate.Phone == request.Phone);
    if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var role = roles.Values.First(role => role.Id == user.RoleId);
    var issuedTokens = IssueTokens(user.Id, role.Code, tokens);
    refreshTokens[issuedTokens.RefreshToken] = user.Id;

    return Results.Ok(issuedTokens);
});

app.MapPost("/api/auth/refresh", (RefreshRequest request, JwtTokenService tokens) =>
{
    if (!refreshTokens.TryGetValue(request.RefreshToken, out var userId) || !users.TryGetValue(userId, out var user))
    {
        return Results.Unauthorized();
    }

    var role = roles.Values.First(role => role.Id == user.RoleId);
    var issuedTokens = IssueTokens(user.Id, role.Code, tokens);
    refreshTokens[issuedTokens.RefreshToken] = user.Id;

    return Results.Ok(issuedTokens);
});

app.MapPost("/api/auth/logout", (RefreshRequest request) =>
{
    refreshTokens.Remove(request.RefreshToken);
    return Results.NoContent();
});

app.MapGet("/api/auth/roles", () => Results.Ok(roles.Values));

app.Run();

static TokenResponse IssueTokens(Guid userId, string role, JwtTokenService tokens)
{
    var refresh = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    return new TokenResponse(userId, tokens.Issue(userId, role, TimeSpan.FromMinutes(30)), refresh);
}

static void SeedAuthUsers(IDictionary<Guid, AuthUser> users, IReadOnlyDictionary<string, Role> roles)
{
    var ownerRole = roles[RoleCodes.Owner];
    var clientRole = roles[RoleCodes.Client];
    users[Guid.Parse("90000000-0000-0000-0000-000000000000")] = new AuthUser(
        Guid.Parse("90000000-0000-0000-0000-000000000000"),
        ownerRole.Id,
        "Owner",
        "+79990000000",
        "owner@hookah.local",
        PasswordHasher.Hash("password"),
        "ACTIVE",
        DateTimeOffset.UtcNow);
    users[Guid.Parse("90000000-0000-0000-0000-000000000001")] = new AuthUser(
        Guid.Parse("90000000-0000-0000-0000-000000000001"),
        clientRole.Id,
        "Client",
        "+79990000001",
        "client@hookah.local",
        PasswordHasher.Hash("password"),
        "ACTIVE",
        DateTimeOffset.UtcNow);
}

static async Task CreateClientProfileAsync(AuthUser user, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var userBaseUrl = configuration["Services:user-service:BaseUrl"] ?? "http://user-service:8080";
    var client = httpClientFactory.CreateClient("user-service");
    try
    {
        await client.PostAsJsonAsync(
            $"{userBaseUrl}/api/users/clients",
            new CreateClientProfileRequest(user.Id, user.Name, user.Phone, user.Email),
            cancellationToken);
    }
    catch
    {
        // The integration event remains the durable source for profile creation when messaging is enabled.
    }
}

public sealed record RegisterRequest(string Name, string Phone, string? Email, string Password);
public sealed record LoginRequest(string Phone, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record TokenResponse(Guid UserId, string AccessToken, string RefreshToken);
public sealed record Role(Guid Id, string Name, string Code, string[] Permissions);
public sealed record AuthUser(Guid Id, Guid RoleId, string Name, string Phone, string? Email, string PasswordHash, string Status, DateTimeOffset CreatedAt);
public sealed record CreateClientProfileRequest(Guid UserId, string Name, string Phone, string? Email);
