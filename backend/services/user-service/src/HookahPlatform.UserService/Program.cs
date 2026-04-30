using HookahPlatform.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("user-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var users = new Dictionary<Guid, UserProfile>();
var shifts = new Dictionary<Guid, StaffShift>();
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
    users[owner.Id] = owner;
    users[master.Id] = master;
    return owner.Id;
}

public sealed record CreateStaffRequest(string Name, string Phone, string Role, Guid BranchId);
public sealed record UpdateUserRequest(string? Name, string? Email, Guid? BranchId, string? Status);
public sealed record UserProfile(Guid Id, string Name, string Phone, string? Email, string Role, Guid? BranchId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record StaffShift(Guid Id, Guid StaffId, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset? ActualStartedAt, DateTimeOffset? ActualFinishedAt, string? RoleOnShift);
public sealed record CreateStaffShiftRequest(Guid StaffId, Guid BranchId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? RoleOnShift);
public sealed record CancelStaffShiftRequest(string Reason);
