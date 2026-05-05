using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.BuildingBlocks.Tenancy;
using HookahPlatform.Contracts;
using HookahPlatform.UserService.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("user-service");
builder.AddPostgresDbContext<UserDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<UserDbContext>("user-service");

app.MapGet("/api/users/me", async (HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var currentUserId = GetForwardedUserId(context);
    if (currentUserId is null)
    {
        return Results.Json(new ProblemDetailsDto("user_context_required", "Forwarded user context is required."), statusCode: StatusCodes.Status401Unauthorized);
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var roles = await LoadRolesAsync(db, tenantId, cancellationToken);
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == currentUserId.Value && candidate.TenantId == tenantId, cancellationToken);
    return user is null ? HttpResults.NotFound("User", currentUserId.Value) : Results.Ok(ToProfile(user, roles));
});

app.MapGet("/api/users", async (string? role, Guid? branchId, string? status, HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var roles = await LoadRolesAsync(db, tenantId, cancellationToken);
    var query = db.Users.AsNoTracking().Where(user => user.TenantId == tenantId);
    var roleCode = string.IsNullOrWhiteSpace(role) ? null : ResolveRoleCode(role, roles);
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

app.MapGet("/api/permissions", async (UserDbContext db, CancellationToken cancellationToken) =>
{
    var permissions = await db.Permissions.AsNoTracking().OrderBy(permission => permission.Code).ToListAsync(cancellationToken);
    return Results.Ok(permissions.Select(permission => new PermissionDefinition(permission.Code, permission.Description ?? string.Empty)));
});

app.MapGet("/api/roles", async (UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var roles = await db.Roles.AsNoTracking()
        .Where(role => role.TenantId == tenantId)
        .OrderBy(role => role.Code)
        .ToListAsync(cancellationToken);

    var roleIds = roles.Select(role => role.Id).ToArray();
    var roleIdToPermissionCodes = await LoadRolePermissionCodesAsync(db, roleIds, cancellationToken);

    var result = roles.Select(role =>
    {
        var permissions = IsSystemOwnerRole(role)
            ? new[] { "*" }
            : (roleIdToPermissionCodes.TryGetValue(role.Id, out var codes) ? codes : Array.Empty<string>());
        return new RoleDto(role.Id, role.Name, role.Code, role.IsSystem, role.IsActive, permissions);
    });

    return Results.Ok(result);
});

app.MapPost("/api/roles", async (CreateRoleRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();

    var name = (request.Name ?? string.Empty).Trim();
    var normalizedCode = NormalizeRoleCode(request.Code);

    if (string.IsNullOrWhiteSpace(name))
    {
        return HttpResults.Validation("Role name is required.");
    }
    if (normalizedCode is null)
    {
        return HttpResults.Validation("Role code is required.");
    }
    if (normalizedCode.Length > 80)
    {
        return HttpResults.Validation("Role code is too long (max 80).");
    }
    if (!RoleCodeRegex().IsMatch(normalizedCode))
    {
        return HttpResults.Validation("Role code must match ^[A-Z0-9_]+$.");
    }

    var exists = await db.Roles.AsNoTracking().AnyAsync(
        role => role.TenantId == tenantId && role.Code == normalizedCode,
        cancellationToken);
    if (exists)
    {
        return HttpResults.Conflict($"Role with code '{normalizedCode}' already exists.");
    }

    var entity = new RoleEntity
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Code = normalizedCode,
        IsSystem = false,
        IsActive = true
    };
    db.Roles.Add(entity);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/roles/{entity.Id}", new RoleDto(entity.Id, entity.Name, entity.Code, entity.IsSystem, entity.IsActive, Array.Empty<string>()));
});

app.MapPatch("/api/roles/{id:guid}", async (Guid id, UpdateRoleRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var role = await db.Roles.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (role is null)
    {
        return HttpResults.NotFound("Role", id);
    }

    if (request.IsActive is false && IsSystemOwnerRole(role))
    {
        return HttpResults.Conflict("System roles cannot be deactivated.");
    }

    if (request.Name is not null)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return HttpResults.Validation("Role name cannot be empty.");
        }

        role.Name = name;
    }

    if (request.IsActive is not null)
    {
        role.IsActive = request.IsActive.Value;
    }

    await db.SaveChangesAsync(cancellationToken);

    var roleIdToPermissionCodes = await LoadRolePermissionCodesAsync(db, new[] { role.Id }, cancellationToken);
    var permissions = IsSystemOwnerRole(role)
        ? new[] { "*" }
        : (roleIdToPermissionCodes.TryGetValue(role.Id, out var codes) ? codes : Array.Empty<string>());

    return Results.Ok(new RoleDto(role.Id, role.Name, role.Code, role.IsSystem, role.IsActive, permissions));
});

app.MapDelete("/api/roles/{id:guid}", async (Guid id, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var role = await db.Roles.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (role is null)
    {
        return HttpResults.NotFound("Role", id);
    }
    if (IsSystemOwnerRole(role))
    {
        return HttpResults.Conflict("System roles cannot be deleted.");
    }

    var referencedByUsers = await db.Users.AsNoTracking().AnyAsync(
        user => user.TenantId == tenantId && user.RoleId == id,
        cancellationToken);
    if (referencedByUsers)
    {
        return HttpResults.Conflict("Role is assigned to users and cannot be deleted.");
    }

    // If DB doesn't have cascade delete on role_permissions, remove links explicitly.
    var roleLinks = db.RolePermissions.Where(link => link.RoleId == id);
    db.RolePermissions.RemoveRange(roleLinks);
    db.Roles.Remove(role);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPut("/api/roles/{id:guid}/permissions", async (Guid id, UpdateRolePermissionsRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var role = await db.Roles.FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (role is null)
    {
        return HttpResults.NotFound("Role", id);
    }

    if (IsSystemOwnerRole(role))
    {
        // OWNER is always wildcard; ignore request and make sure we don't accidentally persist explicit mappings.
        var existing = db.RolePermissions.Where(link => link.RoleId == role.Id);
        db.RolePermissions.RemoveRange(existing);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new RoleDto(role.Id, role.Name, role.Code, role.IsSystem, role.IsActive, new[] { "*" }));
    }

    var requested = (request.Permissions ?? Array.Empty<string>())
        .Select(permission => permission?.Trim())
        .Where(permission => !string.IsNullOrWhiteSpace(permission))
        .Select(permission => permission!)
        .ToArray();

    if (requested.Any(permission => permission.Equals("*", StringComparison.OrdinalIgnoreCase)))
    {
        return HttpResults.Validation("Wildcard '*' is only allowed for OWNER system role.");
    }

    var allPermissions = await db.Permissions.AsNoTracking().ToListAsync(cancellationToken);
    var permissionByCode = allPermissions.ToDictionary(permission => permission.Code, permission => permission, StringComparer.OrdinalIgnoreCase);

    var unknown = requested
        .Where(permission => !permissionByCode.ContainsKey(permission))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (unknown.Length > 0)
    {
        return HttpResults.Validation($"Unknown permissions: {string.Join(", ", unknown)}.");
    }

    var canonicalRequested = requested
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(permission => permissionByCode[permission].Code)
        .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var existingLinks = db.RolePermissions.Where(link => link.RoleId == role.Id);
    db.RolePermissions.RemoveRange(existingLinks);

    foreach (var permission in requested.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        db.RolePermissions.Add(new RolePermissionEntity { RoleId = role.Id, PermissionId = permissionByCode[permission].Id });
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new RoleDto(
        role.Id,
        role.Name,
        role.Code,
        role.IsSystem,
        role.IsActive,
        canonicalRequested));
});

app.MapGet("/api/users/{id:guid}/permissions", async (Guid id, HttpContext context, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (!IsServiceRequest(context) && GetForwardedUserId(context) != id && !HasForwardedPermission(context, PermissionCodes.StaffManage))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "User permissions can only be read by the same user or staff managers."), statusCode: StatusCodes.Status403Forbidden);
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.TenantId == tenantId, cancellationToken);
    if (user is null)
    {
        return HttpResults.NotFound("User", id);
    }

    var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == user.RoleId && candidate.TenantId == tenantId, cancellationToken);
    if (role is null)
    {
        return Results.Ok(new UserPermissions(user.Id, "UNKNOWN", Array.Empty<string>()));
    }

    if (IsSystemOwnerRole(role))
    {
        return Results.Ok(new UserPermissions(user.Id, role.Code, new[] { "*" }));
    }

    var permissions = await db.RolePermissions.AsNoTracking()
        .Where(link => link.RoleId == role.Id)
        .Join(db.Permissions.AsNoTracking(), link => link.PermissionId, permission => permission.Id, (_, permission) => permission.Code)
        .OrderBy(code => code)
        .ToListAsync(cancellationToken);

    return Results.Ok(new UserPermissions(user.Id, role.Code, permissions));
});

app.MapPost("/api/users/staff", async (CreateStaffRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Phone))
    {
        return HttpResults.Validation("Name and phone are required.");
    }

    var tenantId = tenantContext.GetTenantIdOrDemo();
    var roles = await LoadRolesAsync(db, tenantId, cancellationToken);
    var roleCode = ResolveRoleCode(request.Role, roles);
    if (roleCode is null || roleCode == RoleCodes.Client)
    {
        return HttpResults.Validation("Staff role is invalid.");
    }

    var normalizedPhone = request.Phone.Trim();
    if (await db.Users.AnyAsync(user => user.TenantId == tenantId && user.Phone == normalizedPhone, cancellationToken))
    {
        return HttpResults.Conflict("User with this phone already exists.");
    }

    var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Code == roleCode, cancellationToken);
    if (role is null)
    {
        return HttpResults.Validation("Staff role is invalid.");
    }
    var user = new UserEntity
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = request.Name,
        Phone = normalizedPhone,
        Email = null,
        RoleId = role.Id,
        BranchId = request.BranchId,
        PasswordHash = PasswordHasher.Hash(normalizedPhone),
        Status = "active",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/users/{user.Id}", ToProfile(user, roles));
});

app.MapPost("/api/users/clients", async (CreateClientProfileRequest request, UserDbContext db, ITenantContext tenantContext, CancellationToken cancellationToken) =>
{
    var tenantId = tenantContext.GetTenantIdOrDemo();
    var roles = await LoadRolesAsync(db, tenantId, cancellationToken);
    var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == request.UserId && user.TenantId == tenantId, cancellationToken);
    if (existing is not null)
    {
        return Results.Ok(ToProfile(existing, roles));
    }

    var clientRole = await db.Roles.AsNoTracking().FirstAsync(role => role.TenantId == tenantId && role.Code == RoleCodes.Client, cancellationToken);
    var normalizedPhone = request.Phone.Trim();
    var user = new UserEntity
    {
        Id = request.UserId,
        TenantId = tenantId,
        Name = request.Name,
        Phone = normalizedPhone,
        Email = request.Email,
        RoleId = clientRole.Id,
        BranchId = null,
        PasswordHash = PasswordHasher.Hash(normalizedPhone),
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

    var roles = await LoadRolesAsync(db, tenantId, cancellationToken);
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

static async Task<Dictionary<Guid, string>> LoadRolesAsync(UserDbContext db, Guid tenantId, CancellationToken cancellationToken)
{
    return await db.Roles.AsNoTracking()
        .Where(role => role.TenantId == tenantId)
        .ToDictionaryAsync(role => role.Id, role => role.Code, cancellationToken);
}

static UserProfile ToProfile(UserEntity user, IReadOnlyDictionary<Guid, string> roles)
{
    var role = roles.TryGetValue(user.RoleId, out var code) ? code : "UNKNOWN";
    return new UserProfile(user.Id, user.Name, user.Phone, user.Email, role, user.BranchId, user.Status, user.CreatedAt, user.UpdatedAt);
}

static string? ResolveRoleCode(string role, IReadOnlyDictionary<Guid, string> roles)
{
    // Supports filter inputs like "Hookah master" or "HOOKAH_MASTER" or "hookahmaster".
    var normalized = role.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return null;
    }

    // Fast path: exact code match among tenant roles.
    var byCode = roles.Values.FirstOrDefault(code => code.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    if (byCode is not null)
    {
        return byCode;
    }

    // Backward-compatible fallback: allow role name / canonical mappings from the catalog (but still validate against tenant roles above).
    var catalogResolved = RolePermissionCatalog.Roles.FirstOrDefault(candidate =>
        candidate.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
        candidate.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
        candidate.Code.Replace("_", string.Empty).Equals(normalized, StringComparison.OrdinalIgnoreCase))?.Code;
    return catalogResolved;
}

static Guid? GetForwardedUserId(HttpContext context)
{
    return Guid.TryParse(context.Request.Headers[ServiceAccessControl.UserIdHeader].ToString(), out var userId)
        ? userId
        : null;
}

static bool IsServiceRequest(HttpContext context)
{
    // Defense in depth: service context is only trusted when the internal service secret is valid.
    var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
    var expectedSecret = configuration["Security:InternalServiceSecret"];
    if (string.IsNullOrWhiteSpace(expectedSecret)) return false;

    var serviceName = context.Request.Headers[ServiceAccessControl.ServiceNameHeader].ToString();
    var serviceSecret = context.Request.Headers[ServiceAccessControl.ServiceSecretHeader].ToString();
    return !string.IsNullOrWhiteSpace(serviceName) &&
           FixedTimeEquals(serviceSecret, expectedSecret);
}

static bool FixedTimeEquals(string actual, string expected)
{
    var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
    var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
    return actualBytes.Length == expectedBytes.Length &&
           System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
}

static bool HasForwardedPermission(HttpContext context, string permission)
{
    var permissions = context.Request.Headers[ServiceAccessControl.UserPermissionsHeader].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return permissions.Contains("*", StringComparer.OrdinalIgnoreCase) ||
           permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

static string? NormalizeRoleCode(string code)
{
    var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
    return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
}

static bool IsSystemOwnerRole(RoleEntity role)
{
    return role.IsSystem && role.Code.Equals(RoleCodes.Owner, StringComparison.OrdinalIgnoreCase);
}

static async Task<Dictionary<Guid, string[]>> LoadRolePermissionCodesAsync(UserDbContext db, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
{
    if (roleIds.Count == 0)
    {
        return new Dictionary<Guid, string[]>();
    }

    var pairs = await db.RolePermissions.AsNoTracking()
        .Where(link => roleIds.Contains(link.RoleId))
        .Join(db.Permissions.AsNoTracking(), link => link.PermissionId, permission => permission.Id, (link, permission) => new { link.RoleId, permission.Code })
        .ToListAsync(cancellationToken);

    return pairs
        .GroupBy(value => value.RoleId)
        .ToDictionary(
            group => group.Key,
            group => group.Select(value => value.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToArray());
}

static Regex RoleCodeRegex()
{
    return new Regex("^[A-Z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

public sealed record CreateStaffRequest(string Name, string Phone, string Role, Guid BranchId);
public sealed record CreateClientProfileRequest(Guid UserId, string Name, string Phone, string? Email);
public sealed record UpdateUserRequest(string? Name, string? Email, Guid? BranchId, string? Status);
public sealed record UserProfile(Guid Id, string Name, string Phone, string? Email, string Role, Guid? BranchId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record BookingEligibility(Guid UserId, bool IsEligible, string? Reason);
public sealed record UserPermissions(Guid UserId, string Role, IEnumerable<string> Permissions);
public sealed record RoleDto(Guid Id, string Name, string Code, bool IsSystem, bool IsActive, IReadOnlyCollection<string> Permissions);
public sealed record CreateRoleRequest(string Name, string Code);
public sealed record UpdateRoleRequest(string? Name, bool? IsActive);
public sealed record UpdateRolePermissionsRequest(IReadOnlyCollection<string> Permissions);
public sealed record CreateStaffShiftRequest(Guid StaffId, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? RoleOnShift);
public sealed record CancelStaffShiftRequest(string Reason);
