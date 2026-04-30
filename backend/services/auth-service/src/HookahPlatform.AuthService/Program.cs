using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("auth-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var roles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase)
{
    ["Owner"] = new(Guid.NewGuid(), "Owner", "OWNER", ["*"]),
    ["Manager"] = new(Guid.NewGuid(), "Manager", "MANAGER", ["branches.manage", "orders.manage", "inventory.manage"]),
    ["HookahMaster"] = new(Guid.NewGuid(), "Hookah master", "HOOKAH_MASTER", ["orders.prepare", "mixes.read"]),
    ["Client"] = new(Guid.NewGuid(), "Client", "CLIENT", ["bookings.create", "orders.read"])
};

var users = new Dictionary<Guid, AuthUser>();
var refreshTokens = new Dictionary<string, Guid>();

app.MapPost("/api/auth/register", async (RegisterRequest request, IEventPublisher events, JwtTokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Password))
    {
        return HttpResults.Validation("Phone and password are required.");
    }

    if (users.Values.Any(user => user.Phone == request.Phone))
    {
        return HttpResults.Conflict("User with this phone already exists.");
    }

    var role = roles["Client"];
    var user = new AuthUser(Guid.NewGuid(), role.Id, request.Name, request.Phone, request.Email, PasswordHasher.Hash(request.Password), "ACTIVE", DateTimeOffset.UtcNow);
    users[user.Id] = user;

    var issuedTokens = IssueTokens(user.Id, role.Code, tokens);
    refreshTokens[issuedTokens.RefreshToken] = user.Id;

    await events.PublishAsync(new UserRegistered(user.Id, user.Phone, role.Code, DateTimeOffset.UtcNow));

    return Results.Ok(new RegisterResponse(user.Id, issuedTokens.AccessToken, issuedTokens.RefreshToken));
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
    return new TokenResponse(tokens.Issue(userId, role, TimeSpan.FromMinutes(30)), refresh);
}

public sealed record RegisterRequest(string Name, string Phone, string? Email, string Password);
public sealed record LoginRequest(string Phone, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record RegisterResponse(Guid UserId, string AccessToken, string RefreshToken);
public sealed record TokenResponse(string AccessToken, string RefreshToken);
public sealed record Role(Guid Id, string Name, string Code, string[] Permissions);
public sealed record AuthUser(Guid Id, Guid RoleId, string Name, string Phone, string? Email, string PasswordHash, string Status, DateTimeOffset CreatedAt);
