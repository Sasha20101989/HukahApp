using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.UserService.Persistence;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("user-service");
builder.AddPostgresDbContext<UserDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<UserDbContext>("user-service");

var users = new Dictionary<Guid, UserProfile>();
var shifts = new Dictionary<Guid, StaffShift>();
var rolePermissions = RolePermissionCatalog.Roles.ToDictionary(
    role => role.Code,
    role => role.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
    StringComparer.OrdinalIgnoreCase);
var currentUserId = SeedUsers(users);

app.MapGet("/api/users/me", () => Results.Ok(users[currentUserId]));

app.MapGet("/api/users", (string? role, Guid? branchId, string? status) =>
{
    var query = users.Values.AsEnumerable();

    if (!string.IsNullOrWhiteSpace(role))
    {
        query = query.Where(user => string.Equals(user.Role, role, StringComparison.OrdinalIgnoreCase));
    }

    if (branchId is not null)
    {
        query = query.Where(user => user.BranchId == branchId);
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(user => string.Equals(user.Status, status, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.OrderBy(user => user.Name));
});

app.MapGet("/api/roles", () => Results.Ok(RolePermissionCatalog.Roles.Select(role =>
    new RoleView(role.Name, role.Code, rolePermissions[role.Code].OrderBy(permission => permission)))));

app.MapGet("/api/permissions", () => Results.Ok(RolePermissionCatalog.Permissions.OrderBy(permission => permission.Code)));

app.MapGet("/api/users/{id:guid}/permissions", (Guid id) =>
{
    if (!users.TryGetValue(id, out var user))
    {
        return HttpResults.NotFound("User", id);
    }

    var roleCode = ResolveRoleCode(user.Role);
    if (roleCode is null)
    {
        return HttpResults.Validation($"Unknown role '{user.Role}'.");
    }

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

app.MapPost("/api/users/staff", (CreateStaffRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Phone))
    {
        return HttpResults.Validation("Name and phone are required.");
    }

    var user = new UserProfile(Guid.NewGuid(), request.Name, request.Phone, null, request.Role, request.BranchId, "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    users[user.Id] = user;

    return Results.Created($"/api/users/{user.Id}", user);
});

app.MapPost("/api/users/clients", (CreateClientProfileRequest request) =>
{
    if (users.ContainsKey(request.UserId))
    {
        return Results.Ok(users[request.UserId]);
    }

    var user = new UserProfile(request.UserId, request.Name, request.Phone, request.Email, "Client", null, "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    users[user.Id] = user;
    return Results.Created($"/api/users/{user.Id}", user);
});

app.MapPatch("/api/users/{id:guid}", (Guid id, UpdateUserRequest request) =>
{
    if (!users.TryGetValue(id, out var user))
    {
        return HttpResults.NotFound("User", id);
    }

    var updated = user with
    {
        Name = request.Name ?? user.Name,
        Email = request.Email ?? user.Email,
        BranchId = request.BranchId ?? user.BranchId,
        Status = request.Status ?? user.Status,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    users[id] = updated;
    return Results.Ok(updated);
});

app.MapGet("/api/users/{id:guid}/booking-eligibility", (Guid id) =>
{
    if (!users.TryGetValue(id, out var user))
    {
        return HttpResults.NotFound("User", id);
    }

    var isEligible = user.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
        && !user.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase);

    return Results.Ok(new BookingEligibility(user.Id, isEligible, isEligible ? null : "Client is blocked or inactive."));
});

app.MapDelete("/api/users/{id:guid}", (Guid id) =>
{
    if (!users.TryGetValue(id, out var user))
    {
        return HttpResults.NotFound("User", id);
    }

    users[id] = user with { Status = "inactive", UpdatedAt = DateTimeOffset.UtcNow };
    return Results.NoContent();
});

app.MapGet("/api/staff/shifts", (Guid? branchId, Guid? staffId, DateOnly? from, DateOnly? to, string? status) =>
{
    var query = shifts.Values.AsEnumerable();
    if (branchId is not null)
    {
        query = query.Where(shift => shift.BranchId == branchId);
    }
    if (staffId is not null)
    {
        query = query.Where(shift => shift.StaffId == staffId);
    }
    if (from is not null)
    {
        query = query.Where(shift => DateOnly.FromDateTime(shift.StartsAt.UtcDateTime) >= from);
    }
    if (to is not null)
    {
        query = query.Where(shift => DateOnly.FromDateTime(shift.StartsAt.UtcDateTime) <= to);
    }
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(shift => string.Equals(shift.Status, status, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.OrderBy(shift => shift.StartsAt));
});

app.MapPost("/api/staff/shifts", (CreateStaffShiftRequest request) =>
{
    if (!users.TryGetValue(request.StaffId, out var staff))
    {
        return HttpResults.NotFound("User", request.StaffId);
    }

    if (staff.BranchId != request.BranchId)
    {
        return HttpResults.Validation("Staff member must be assigned to the shift branch.");
    }

    if (request.StartsAt >= request.EndsAt)
    {
        return HttpResults.Validation("Shift startsAt must be earlier than endsAt.");
    }

    var intersects = shifts.Values.Any(shift =>
        shift.StaffId == request.StaffId &&
        shift.Status is not "CANCELLED" &&
        request.StartsAt < shift.EndsAt &&
        request.EndsAt > shift.StartsAt);

    if (intersects)
    {
        return HttpResults.Conflict("Staff shift intersects with an existing shift.");
    }

    var created = new StaffShift(Guid.NewGuid(), request.StaffId, request.BranchId, request.StartsAt, request.EndsAt, "PLANNED", null, null, request.RoleOnShift);
    shifts[created.Id] = created;
    return Results.Created($"/api/staff/shifts/{created.Id}", created);
});

app.MapPatch("/api/staff/shifts/{id:guid}/start", (Guid id) =>
{
    if (!shifts.TryGetValue(id, out var shift))
    {
        return HttpResults.NotFound("StaffShift", id);
    }

    shifts[id] = shift with { Status = "ACTIVE", ActualStartedAt = DateTimeOffset.UtcNow };
    return Results.Ok(shifts[id]);
});

app.MapPatch("/api/staff/shifts/{id:guid}/finish", (Guid id) =>
{
    if (!shifts.TryGetValue(id, out var shift))
    {
        return HttpResults.NotFound("StaffShift", id);
    }

    shifts[id] = shift with { Status = "COMPLETED", ActualFinishedAt = DateTimeOffset.UtcNow };
    return Results.Ok(shifts[id]);
});

app.MapPatch("/api/staff/shifts/{id:guid}/cancel", (Guid id, CancelStaffShiftRequest request) =>
{
    if (!shifts.TryGetValue(id, out var shift))
    {
        return HttpResults.NotFound("StaffShift", id);
    }

    shifts[id] = shift with { Status = "CANCELLED", RoleOnShift = request.Reason };
    return Results.Ok(shifts[id]);
});

app.Run();

static Guid SeedUsers(IDictionary<Guid, UserProfile> users)
{
    var branchId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var owner = new UserProfile(Guid.NewGuid(), "Owner", "+79990000000", "owner@hookah.local", "Owner", branchId, "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    var master = new UserProfile(Guid.NewGuid(), "Hookah Master", "+79991112233", null, "HookahMaster", branchId, "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    var client = new UserProfile(Guid.Parse("90000000-0000-0000-0000-000000000001"), "Client", "+79990000001", "client@hookah.local", "Client", null, "active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    users[owner.Id] = owner;
    users[master.Id] = master;
    users[client.Id] = client;
    return owner.Id;
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
public sealed record StaffShift(Guid Id, Guid StaffId, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset? ActualStartedAt, DateTimeOffset? ActualFinishedAt, string? RoleOnShift);
public sealed record CreateStaffShiftRequest(Guid StaffId, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? RoleOnShift);
public sealed record CancelStaffShiftRequest(string Reason);
