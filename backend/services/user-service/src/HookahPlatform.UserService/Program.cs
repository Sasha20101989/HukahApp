using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.BuildingBlocks.Tenancy;
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

app.MapGet("/api/users/me", async (HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var currentUserId = GetForwardedUserId(context);
    if (currentUserId is null)
    {
        return Results.Json(new ProblemDetailsDto("user_context_required", "Forwarded user context is required."), statusCode: StatusCodes.Status401Unauthorized);
    }

    var roles = await LoadRolesAsync(db, cancellationToken);
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == currentUserId.Value && candidate.TenantId == tenantId, cancellationToken);
    return user is null ? HttpResults.NotFound("User", currentUserId.Value) : Results.Ok(ToProfile(user, roles));
});

app.MapGet("/api/users", async (string? role, Guid? branchId, string? status, HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var roles = await LoadRolesAsync(db, cancellationToken);
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var query = db.Users.AsNoTracking().Where(user => user.TenantId == tenantId);
    var roleCode = string.IsNullOrWhiteSpace(role) ? null : ResolveRoleCode(role);
    var canManageStaff = IsServiceRequest(context) || HasForwardedPermission(context, PermissionCodes.StaffManage);

    if (!canManageStaff)
    {
        if (roleCode is null || !roleCode.Equals(RoleCodes.Client, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new ProblemDetailsDto("forbidden", "Operational roles can list active clients only."), statusCode: StatusCodes.Status403Forbidden);
        }

        status = "active";
        branchId = null;
    }

    if (!string.IsNullOrWhiteSpace(role))
    {
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

app.MapGet("/api/users/{id:guid}/permissions", async (Guid id, HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (!IsServiceRequest(context) && GetForwardedUserId(context) != id && !HasForwardedPermission(context, PermissionCodes.StaffManage))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "User permissions can only be read by the same user or staff managers."), statusCode: StatusCodes.Status403Forbidden);
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var roles = await LoadRolesAsync(db, cancellationToken);
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
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

app.MapPost("/api/users/staff", async (CreateStaffRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
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

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var role = await db.Roles.AsNoTracking().FirstAsync(candidate => candidate.Code == roleCode, cancellationToken);
    var user = new UserEntity
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
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

app.MapPost("/api/users/clients", async (CreateClientProfileRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var roles = await LoadRolesAsync(db, cancellationToken);
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == request.UserId && user.TenantId == tenantId, cancellationToken);
    if (existing is not null)
    {
        return Results.Ok(ToProfile(existing, roles));
    }

    var clientRole = await db.Roles.AsNoTracking().FirstAsync(role => role.Code == RoleCodes.Client, cancellationToken);
    var user = new UserEntity
    {
        Id = request.UserId,
        TenantId = tenantId,
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

app.MapPatch("/api/users/{id:guid}", async (Guid id, UpdateUserRequest request, HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var canManageUsers = IsServiceRequest(context) || HasForwardedPermission(context, PermissionCodes.StaffManage);
    if (!canManageUsers && GetForwardedUserId(context) != id)
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Users can only update their own profile unless they have staff management permissions."), statusCode: StatusCodes.Status403Forbidden);
    }

    if (!canManageUsers && (request.BranchId is not null || request.Status is not null))
    {
        return HttpResults.Validation("Client profile update can only change name and email.");
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
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

app.MapGet("/api/users/{id:guid}/booking-eligibility", async (Guid id, HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (!IsServiceRequest(context) &&
        GetForwardedUserId(context) != id &&
        !HasForwardedPermission(context, PermissionCodes.BookingsManage) &&
        !HasForwardedPermission(context, PermissionCodes.StaffManage))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Booking eligibility can only be read by the same user or booking/staff managers."), statusCode: StatusCodes.Status403Forbidden);
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (user is null)
    {
        return HttpResults.NotFound("User", id);
    }

    var isEligible = user.Status.Equals("active", StringComparison.OrdinalIgnoreCase);
    return Results.Ok(new BookingEligibility(user.Id, isEligible, isEligible ? null : "Client is blocked or inactive."));
});

app.MapDelete("/api/users/{id:guid}", async (Guid id, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (user is null)
    {
        return HttpResults.NotFound("User", id);
    }

    user.Status = "inactive";
    user.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/staff/shifts", async (Guid? branchId, Guid? staffId, DateOnly? from, DateOnly? to, string? status, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var query = db.StaffShifts.AsNoTracking().Where(shift => shift.TenantId == tenantId);
    if (branchId is not null) query = query.Where(shift => shift.BranchId == branchId);
    if (staffId is not null) query = query.Where(shift => shift.StaffId == staffId);
    if (from is not null) query = query.Where(shift => DateOnly.FromDateTime(shift.StartsAt.UtcDateTime) >= from);
    if (to is not null) query = query.Where(shift => DateOnly.FromDateTime(shift.StartsAt.UtcDateTime) <= to);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(shift => shift.Status == status);

    return Results.Ok(await query.OrderBy(shift => shift.StartsAt).ToListAsync(cancellationToken));
});

app.MapPost("/api/staff/shifts", async (CreateStaffShiftRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var staff = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == request.StaffId && user.TenantId == tenantId, cancellationToken);
    if (staff is null) return HttpResults.NotFound("User", request.StaffId);
    if (staff.BranchId != request.BranchId) return HttpResults.Validation("Staff member must be assigned to the shift branch.");
    if (request.StartsAt >= request.EndsAt) return HttpResults.Validation("Shift startsAt must be earlier than endsAt.");

    var intersects = await db.StaffShifts.AnyAsync(shift =>
        shift.TenantId == tenantId &&
        shift.StaffId == request.StaffId &&
        shift.Status != "CANCELLED" &&
        request.StartsAt < shift.EndsAt &&
        request.EndsAt > shift.StartsAt,
        cancellationToken);
    if (intersects) return HttpResults.Conflict("Staff shift intersects with an existing shift.");

    var created = new StaffShiftEntity { Id = Guid.NewGuid(), TenantId = tenantId, StaffId = request.StaffId, BranchId = request.BranchId, StartsAt = request.StartsAt, EndsAt = request.EndsAt, Status = "PLANNED", ActualStartedAt = null, ActualFinishedAt = null, RoleOnShift = request.RoleOnShift };
    db.StaffShifts.Add(created);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/staff/shifts/{created.Id}", created);
});

app.MapPatch("/api/staff/shifts/{id:guid}/start", async (Guid id, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var shift = await db.StaffShifts.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (shift is null) return HttpResults.NotFound("StaffShift", id);
    shift.Status = "ACTIVE";
    shift.ActualStartedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(shift);
});

app.MapPatch("/api/staff/shifts/{id:guid}/finish", async (Guid id, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var shift = await db.StaffShifts.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (shift is null) return HttpResults.NotFound("StaffShift", id);
    shift.Status = "COMPLETED";
    shift.ActualFinishedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(shift);
});

app.MapPatch("/api/staff/shifts/{id:guid}/cancel", async (Guid id, CancelStaffShiftRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var shift = await db.StaffShifts.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (shift is null) return HttpResults.NotFound("StaffShift", id);
    var reason = request.Reason?.Trim();
    if (string.IsNullOrWhiteSpace(reason)) return HttpResults.Validation("Cancellation reason is required.");
    shift.Status = "CANCELLED";
    shift.CancelReason = reason;
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

static Guid? GetForwardedUserId(HttpContext context)
{
    return Guid.TryParse(context.Request.Headers[ServiceAccessControl.UserIdHeader].ToString(), out var userId)
        ? userId
        : null;
}

static bool IsServiceRequest(HttpContext context)
{
    return !string.IsNullOrWhiteSpace(context.Request.Headers[ServiceAccessControl.ServiceNameHeader].ToString());
}

static bool HasForwardedPermission(HttpContext context, string permission)
{
    var permissions = context.Request.Headers[ServiceAccessControl.UserPermissionsHeader].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return permissions.Contains("*", StringComparer.OrdinalIgnoreCase) ||
           permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
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
