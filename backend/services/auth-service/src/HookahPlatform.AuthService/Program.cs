using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Auditing;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.AuthService.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("auth-service");
builder.AddPostgresDbContext<AuthDbContext>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<AuthDbContext>("auth-service");

app.MapPost("/api/auth/register", async (RegisterRequest request, AuthDbContext db, IEventPublisher events, JwtTokenService tokens, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Password))
    {
        return HttpResults.Validation("Phone and password are required.");
    }

    if (await db.Users.AnyAsync(user => user.Phone == request.Phone, cancellationToken))
    {
        return HttpResults.Conflict("User with this phone already exists.");
    }

    var role = await db.Roles.AsNoTracking().FirstAsync(candidate => candidate.Code == RoleCodes.Client, cancellationToken);
    var user = new AuthUserEntity
    {
        Id = Guid.NewGuid(),
        RoleId = role.Id,
        BranchId = null,
        Name = request.Name,
        Phone = request.Phone,
        Email = request.Email,
        PasswordHash = PasswordHasher.Hash(request.Password),
        Status = "active",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(user);
    // User.Id is client-generated GUID, so EF may treat it as "already exists" and try to insert
    // dependent rows (refresh_tokens) first. Persist the user first to satisfy FK constraints.
    await db.SaveChangesAsync(cancellationToken);

    var issuedTokens = IssueTokens(user.Id, role.Code, tokens);
    var refreshToken = CreateRefreshToken(user.Id, issuedTokens.RefreshToken);
    db.RefreshTokens.Add(refreshToken);
    var registered = new UserRegistered(user.Id, user.Phone, role.Code, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(registered);
    await db.SaveChangesAsync(cancellationToken);

    await CreateClientProfileAsync(user, httpClientFactory, configuration, cancellationToken);
    await StoreRefreshSessionAsync(cache, refreshToken, role.Code, cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, registered, outboxMessage, cancellationToken);

    return Results.Ok(issuedTokens);
});

app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, AuthDbContext db, JwtTokenService tokens, IDistributedCache cache, IAuditLogWriter audit, CancellationToken cancellationToken) =>
{
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Phone == request.Phone, cancellationToken);
    if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
    {
        await audit.WriteAsync(null, user?.Id, "auth.login", "user", user?.Id.ToString(), "failure", AuditLogContext.CorrelationId(context), new { request.Phone }, cancellationToken);
        return Results.Unauthorized();
    }

    var role = await db.Roles.AsNoTracking().FirstAsync(candidate => candidate.Id == user.RoleId, cancellationToken);
    var issuedTokens = IssueTokens(user.Id, role.Code, tokens);
    var refreshToken = CreateRefreshToken(user.Id, issuedTokens.RefreshToken);
    db.RefreshTokens.Add(refreshToken);
    await db.SaveChangesAsync(cancellationToken);
    await StoreRefreshSessionAsync(cache, refreshToken, role.Code, cancellationToken);
    await audit.WriteAsync(null, user.Id, "auth.login", "user", user.Id.ToString(), "success", AuditLogContext.CorrelationId(context), new { role.Code }, cancellationToken);

    return Results.Ok(issuedTokens);
});

app.MapPost("/api/auth/refresh", async (RefreshRequest request, HttpContext context, AuthDbContext db, JwtTokenService tokens, IDistributedCache cache, IAuditLogWriter audit, CancellationToken cancellationToken) =>
{
    var tokenHash = HashRefreshToken(request.RefreshToken);
    var cachedSession = await cache.GetJsonAsync<AuthRefreshSession>(RefreshSessionKey(tokenHash), cancellationToken);
    if (cachedSession is { RevokedAt: null } && cachedSession.ExpiresAt <= DateTimeOffset.UtcNow)
    {
        await cache.RemoveAsync(RefreshSessionKey(tokenHash), cancellationToken);
        await audit.WriteAsync(null, cachedSession.UserId, "auth.refresh", "refresh_token", null, "failure", AuditLogContext.CorrelationId(context), new { reason = "expired_cache" }, cancellationToken);
        return Results.Unauthorized();
    }
    if (cachedSession is { RevokedAt: not null })
    {
        await audit.WriteAsync(null, cachedSession.UserId, "auth.refresh", "refresh_token", null, "failure", AuditLogContext.CorrelationId(context), new { reason = "revoked_cache" }, cancellationToken);
        return Results.Unauthorized();
    }

    var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(candidate =>
        candidate.TokenHash == tokenHash &&
        candidate.RevokedAt == null &&
        candidate.ExpiresAt > DateTimeOffset.UtcNow,
        cancellationToken);
    if (storedToken is null)
    {
        await audit.WriteAsync(null, null, "auth.refresh", "refresh_token", null, "failure", AuditLogContext.CorrelationId(context), new { reason = "not_found" }, cancellationToken);
        return Results.Unauthorized();
    }

    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == storedToken.UserId, cancellationToken);
    if (user is null)
    {
        await audit.WriteAsync(null, storedToken.UserId, "auth.refresh", "refresh_token", storedToken.Id.ToString(), "failure", AuditLogContext.CorrelationId(context), new { reason = "user_not_found" }, cancellationToken);
        return Results.Unauthorized();
    }

    var role = await db.Roles.AsNoTracking().FirstAsync(candidate => candidate.Id == user.RoleId, cancellationToken);
    var issuedTokens = IssueTokens(user.Id, role.Code, tokens);
    storedToken.RevokedAt = DateTimeOffset.UtcNow;
    var refreshToken = CreateRefreshToken(user.Id, issuedTokens.RefreshToken);
    db.RefreshTokens.Add(refreshToken);
    await db.SaveChangesAsync(cancellationToken);
    await RevokeRefreshSessionAsync(cache, storedToken, role.Code, cancellationToken);
    await StoreRefreshSessionAsync(cache, refreshToken, role.Code, cancellationToken);
    await audit.WriteAsync(null, user.Id, "auth.refresh", "refresh_token", storedToken.Id.ToString(), "success", AuditLogContext.CorrelationId(context), new { role.Code }, cancellationToken);

    return Results.Ok(issuedTokens);
});

app.MapPost("/api/auth/logout", async (RefreshRequest request, HttpContext context, AuthDbContext db, IDistributedCache cache, IAuditLogWriter audit, CancellationToken cancellationToken) =>
{
    var tokenHash = HashRefreshToken(request.RefreshToken);
    var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash && candidate.RevokedAt == null, cancellationToken);
    if (storedToken is not null)
    {
        storedToken.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
    await cache.RemoveAsync(RefreshSessionKey(tokenHash), cancellationToken);
    await audit.WriteAsync(null, storedToken?.UserId, "auth.logout", "refresh_token", storedToken?.Id.ToString(), storedToken is null ? "failure" : "success", AuditLogContext.CorrelationId(context), null, cancellationToken);

    return Results.NoContent();
});

app.MapGet("/api/auth/roles", () => Results.Ok(RolePermissionCatalog.Roles.Select(role => new Role(role.Name, role.Code, role.Permissions.ToArray()))));

app.Run();

static TokenResponse IssueTokens(Guid userId, string role, JwtTokenService tokens)
{
    var refresh = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    return new TokenResponse(userId, tokens.Issue(userId, role, TimeSpan.FromMinutes(30)), refresh);
}

static AuthRefreshTokenEntity CreateRefreshToken(Guid userId, string refreshToken)
{
    var now = DateTimeOffset.UtcNow;
    return new AuthRefreshTokenEntity
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = HashRefreshToken(refreshToken),
        CreatedAt = now,
        ExpiresAt = now.AddDays(30),
        RevokedAt = null
    };
}

static string HashRefreshToken(string refreshToken)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
    return Convert.ToHexString(bytes);
}

static string RefreshSessionKey(string tokenHash) => $"auth:refresh:{tokenHash}";

static async Task StoreRefreshSessionAsync(IDistributedCache cache, AuthRefreshTokenEntity token, string role, CancellationToken cancellationToken)
{
    var ttl = token.ExpiresAt - DateTimeOffset.UtcNow;
    if (ttl <= TimeSpan.Zero) return;

    await cache.SetJsonAsync(
        RefreshSessionKey(token.TokenHash),
        new AuthRefreshSession(token.UserId, role, token.CreatedAt, token.ExpiresAt, token.RevokedAt),
        ttl,
        cancellationToken);
}

static Task RevokeRefreshSessionAsync(IDistributedCache cache, AuthRefreshTokenEntity token, string role, CancellationToken cancellationToken)
{
    return cache.SetJsonAsync(
        RefreshSessionKey(token.TokenHash),
        new AuthRefreshSession(token.UserId, role, token.CreatedAt, token.ExpiresAt, token.RevokedAt),
        TimeSpan.FromMinutes(5),
        cancellationToken);
}

static async Task CreateClientProfileAsync(AuthUserEntity user, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
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
public sealed record Role(string Name, string Code, string[] Permissions);
public sealed record CreateClientProfileRequest(Guid UserId, string Name, string Phone, string? Email);
public sealed record AuthRefreshSession(Guid UserId, string Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
