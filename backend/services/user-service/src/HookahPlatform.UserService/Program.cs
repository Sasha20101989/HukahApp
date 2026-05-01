using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.Contracts;
using HookahPlatform.UserService.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("user-service");
builder.AddPostgresDbContext<UserDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<UserDbContext>("user-service");

var rolePermissions = RolePermissionCatalog.Roles.ToDictionary(
    role => role.Code,
    role => role.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
    StringComparer.OrdinalIgnoreCase);
var currentUserId = Guid.Parse("90000000-0000-0000-0000-000000000000");

app.MapGet("/api/users/me", async (UserDbContext db, CancellationToken cancellationToken) =>
{
    var roles = await LoadRolesAsync(db, cancellationToken);
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);
    return user is null ? HttpResults.NotFound("User", currentUserId) : Results.Ok(ToProfile(user, roles));
});

app.MapGet("/api/users", async (string? role, Guid? branchId, string? status, UserDbContext db, CancellationToken cancellationToken) =>
{
    var roles = await LoadRolesAsync(db, cancellationToken);
    var query = db.Users.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(role))
    {
        var roleCode = ResolveRoleCode(role);
        if (roleCode is null || !roles.Values.Any(candidate => candidate.Equals(roleCode, StringComparison.OrdinalIgnoreCase)))
        {
            return HttpResults.Validation($"Unknown role '{role}'.");
        }

        var roleId = roles.First(candidate => candidate.Value.Equals(roleCode, StringComparison.OrdinalIgnoreCase)).Key;
        query = query.Where(user => user.RoleId == roleId);
    }
    if (branchId is not null)
    {
        query = query.Where(user => user.BranchId == branchId);
    }
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(user => user.Status == status);
    }

    var users = await query.OrderBy(user => user.Name).ToListAsync(cancellationToken);
    return Results.Ok(users.Select(user => ToProfile(user, roles)));
});

app.MapGet("/api/roles", () => Results.Ok(RolePermissionCatalog.Roles.Select(role =>
    new RoleView(role.Name, role.Code, rolePermissions[role.Code].OrderBy(permission => permission)))));

app.MapGet("/api/permissions", () => Results.Ok(RolePermissionCatalog.Permissions.OrderBy(permission => permission.Code)));

app.MapGet("/api/users/{id:guid}/permissions", async (Guid id, UserDbContext db, CancellationToken cancellationToken) =>
{
    var roles = await LoadRolesAsync(db, cancellationToken);
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (user is null)
    {
        return HttpResults.NotFound("User", id);
    }

    var roleCode = roles[user.RoleId];
    return Results.Ok(new UserPermissions(user.Id, roleCode, rolePermissions[roleCode].OrderBy(permission => permission)));
});

app.MapPatch("/api/roles/{code}/permissions", (string code, UpdateRolePermissionsRequest request) =>
{
    var roleCode = ResolveRoleCode(code);
    if (roleCode is null)
    {
        return HttpResults.NotFound("Role", Guid.Empty);
    }

    var allowedPermissions = RolePermissionCatalog.Permissions
        .Select(permission => permission.Code)
        .Append("*")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var requestedPermissions = request.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var unknownPermissions = requestedPermissions.Where(permission => !allowedPermissions.Contains(permission)).ToArray();
    if (unknownPermissions.Length > 0)
    {
        return HttpResults.Validation($"Unknown permissions: {string.Join(", ", unknownPermissions)}.");
    }

    rolePermissions[roleCode] = requestedPermissions;
    var role = RolePermissionCatalog.Roles.First(role => role.Code.Equals(roleCode, StringComparison.OrdinalIgnoreCase));
    return Results.Ok(new RoleView(role.Name, role.Code, rolePermissions[roleCode].OrderBy(permission => permission)));
});

app.MapPost("/api/users/staff", async (CreateStaffRequest request, UserDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Phone))
    {
        return HttpResults.Validation("Name and phone are required.");
    }

    var roleCode = ResolveRoleCode(request.Role);
    if (roleCode is null || roleCode == RoleCodes.Client)
    {
        return HttpResults.Validation("Staff role is invalid.");
    }

    if (await db.Users.AnyAsync(user => user.Phone == request.Phone, cancellationToken))
    {
        return HttpResults.Conflict("User with this phone already exists.");
    }

    var role = await db.Roles.AsNoTracking().FirstAsync(candidate => candidate.Code == roleCode, cancellationToken);
    var user = new UserEntity
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Phone = request.Phone,
        Email = null,
        RoleId = role.Id,
        BranchId = request.BranchId,
        PasswordHash = PasswordHasher.Hash(request.Phone),
        Status = "active",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);

    var roles = await LoadRolesAsync(db, cancellationToken);
    return Results.Created($"/api/users/{user.Id}", ToProfile(user, roles));
});

app.MapPost("/api/users/clients", async (CreateClientProfileRequest request, UserDbContext db, CancellationToken cancellationToken) =>
{
    var roles = await LoadRolesAsync(db, cancellationToken);
    var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == request.UserId, cancellationToken);
    if (existing is not null)
    {
        return Results.Ok(ToProfile(existing, roles));
    }

    var clientRole = await db.Roles.AsNoTracking().FirstAsync(role => role.Code == RoleCodes.Client, cancellationToken);
    var user = new UserEntity
    {
        Id = request.UserId,
        Name = request.Name,
        Phone = request.Phone,
        Email = request.Email,
        RoleId = clientRole.Id,
        BranchId = null,
        PasswordHash = PasswordHasher.Hash(request.Phone),
        Status = "active",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);
    roles[user.RoleId] = clientRole.Code;
    return Results.Created($"/api/users/{user.Id}", ToProfile(user, roles));
});

app.MapPatch("/api/users/{id:guid}", async (Guid id, UpdateUserRequest request, UserDbContext db, CancellationToken cancellationToken) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (user is null)
    {
        return HttpResults.NotFound("User", id);
    }

    user.Name = request.Name ?? user.Name;
    user.Email = request.Email ?? user.Email;
    user.BranchId = request.BranchId ?? user.BranchId;
    user.Status = request.Status ?? user.Status;
    user.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);

    var roles = await LoadRolesAsync(db, cancellationToken);
    return Results.Ok(ToProfile(user, roles));
});

app.MapGet("/api/users/{id:guid}/booking-eligibility", async (Guid id, UserDbContext db, CancellationToken cancellationToken) =>
{
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (user is null)
    {
        return HttpResults.NotFound("User", id);
    }

    var isEligible = user.Status.Equals("active", StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new BookingEligibility(user.Id, isEligible, isEligible ? null : "Client is blocked or inactive."));
});

app.MapDelete("/api/users/{id:guid}", async (Guid id, UserDbContext db, CancellationToken cancellationToken) =>
{
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (user is null)
    {
        return HttpResults.NotFound("User", id);
    }

    user.Status = "inactive";
    user.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/staff/shifts", async (Guid? branchId, Guid? staffId, DateOnly? from, DateOnly? to, string? status, UserDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.StaffShifts.AsNoTracking();
    if (branchId is not null) query = query.Where(shift => shift.BranchId == branchId);
    if (staffId is not null) query = query.Where(shift => shift.StaffId == staffId);
    if (from is not null) query = query.Where(shift => DateOnly.FromDateTime(shift.StartsAt.UtcDateTime) >= from);
    if (to is not null) query = query.Where(shift => DateOnly.FromDateTime(shift.StartsAt.UtcDateTime) <= to);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(shift => shift.Status == status);

    return Results.Ok(await query.OrderBy(shift => shift.StartsAt).ToListAsync(cancellationToken));
});

app.MapPost("/api/staff/shifts", async (CreateStaffShiftRequest request, UserDbContext db, CancellationToken cancellationToken) =>
{
    var staff = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == request.StaffId, cancellationToken);
    if (staff is null) return HttpResults.NotFound("User", request.StaffId);
    if (staff.BranchId != request.BranchId) return HttpResults.Validation("Staff member must be assigned to the shift branch.");
    if (request.StartsAt >= request.EndsAt) return HttpResults.Validation("Shift startsAt must be earlier than endsAt.");

    var intersects = await db.StaffShifts.AnyAsync(shift =>
        shift.StaffId == request.StaffId &&
        shift.Status != "CANCELLED" &&
        request.StartsAt < shift.EndsAt &&
        request.EndsAt > shift.StartsAt,
        cancellationToken);
    if (intersects) return HttpResults.Conflict("Staff shift intersects with an existing shift.");

    var created = new StaffShiftEntity { Id = Guid.NewGuid(), StaffId = request.StaffId, BranchId = request.BranchId, StartsAt = request.StartsAt, EndsAt = request.EndsAt, Status = "PLANNED", ActualStartedAt = null, ActualFinishedAt = null, RoleOnShift = request.RoleOnShift };
    db.StaffShifts.Add(created);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/staff/shifts/{created.Id}", created);
});

app.MapPatch("/api/staff/shifts/{id:guid}/start", async (Guid id, UserDbContext db, CancellationToken cancellationToken) =>
{
    var shift = await db.StaffShifts.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (shift is null) return HttpResults.NotFound("StaffShift", id);
    shift.Status = "ACTIVE";
    shift.ActualStartedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(shift);
});

app.MapPatch("/api/staff/shifts/{id:guid}/finish", async (Guid id, UserDbContext db, CancellationToken cancellationToken) =>
{
    var shift = await db.StaffShifts.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (shift is null) return HttpResults.NotFound("StaffShift", id);
    shift.Status = "COMPLETED";
    shift.ActualFinishedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(shift);
});

app.MapPatch("/api/staff/shifts/{id:guid}/cancel", async (Guid id, CancelStaffShiftRequest request, UserDbContext db, CancellationToken cancellationToken) =>
{
    var shift = await db.StaffShifts.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (shift is null) return HttpResults.NotFound("StaffShift", id);
    shift.Status = "CANCELLED";
    shift.CancelReason = request.Reason;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(shift);
});

app.Run();

static async Task<Dictionary<Guid, string>> LoadRolesAsync(UserDbContext db, CancellationToken cancellationToken)
{
    return await db.Roles.AsNoTracking().ToDictionaryAsync(role => role.Id, role => role.Code, cancellationToken);
}

static UserProfile ToProfile(UserEntity user, IReadOnlyDictionary<Guid, string> roles)
{
    var role = roles.TryGetValue(user.RoleId, out var code) ? code : "UNKNOWN";
    return new UserProfile(user.Id, user.Name, user.Phone, user.Email, role, user.BranchId, user.Status, user.CreatedAt, user.UpdatedAt);
}

static string? ResolveRoleCode(string role)
{
    return RolePermissionCatalog.Roles.FirstOrDefault(candidate =>
        candidate.Code.Equals(role, StringComparison.OrdinalIgnoreCase) ||
        candidate.Name.Equals(role, StringComparison.OrdinalIgnoreCase) ||
        candidate.Code.Replace("_", string.Empty).Equals(role, StringComparison.OrdinalIgnoreCase))?.Code;
}

public sealed record CreateStaffRequest(string Name, string Phone, string Role, Guid BranchId);
public sealed record CreateClientProfileRequest(Guid UserId, string Name, string Phone, string? Email);
public sealed record UpdateUserRequest(string? Name, string? Email, Guid? BranchId, string? Status);
public sealed record UserProfile(Guid Id, string Name, string Phone, string? Email, string Role, Guid? BranchId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record BookingEligibility(Guid UserId, bool IsEligible, string? Reason);
public sealed record RoleView(string Name, string Code, IEnumerable<string> Permissions);
public sealed record UserPermissions(Guid UserId, string Role, IEnumerable<string> Permissions);
public sealed record UpdateRolePermissionsRequest(IReadOnlyCollection<string> Permissions);
public sealed record CreateStaffShiftRequest(Guid StaffId, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? RoleOnShift);
public sealed record CancelStaffShiftRequest(string Reason);
