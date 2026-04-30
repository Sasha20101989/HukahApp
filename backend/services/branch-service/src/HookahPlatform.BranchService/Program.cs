using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BranchService.Persistence;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("branch-service");
builder.AddPostgresDbContext<BranchDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<BranchDbContext>("branch-service");

var branches = new Dictionary<Guid, Branch>();
var halls = new Dictionary<Guid, Hall>();
var zones = new Dictionary<Guid, Zone>();
var tables = new Dictionary<Guid, TableSeat>();
var hookahs = new Dictionary<Guid, Hookah>();
var workingHours = new Dictionary<(Guid BranchId, DayOfWeek DayOfWeek), BranchWorkingHours>();
SeedBranches(branches, halls, zones, tables, hookahs, workingHours);

app.MapGet("/api/branches", () => Results.Ok(branches.Values.OrderBy(branch => branch.Name)));

app.MapPost("/api/branches", (CreateBranchRequest request) =>
{
    var branch = new Branch(Guid.NewGuid(), request.Name, request.Address, request.Phone, request.Timezone, true, DateTimeOffset.UtcNow);
    branches[branch.Id] = branch;
    return Results.Created($"/api/branches/{branch.Id}", branch);
});

app.MapGet("/api/branches/{id:guid}", (Guid id) =>
    branches.TryGetValue(id, out var branch) ? Results.Ok(branch) : HttpResults.NotFound("Branch", id));

app.MapPatch("/api/branches/{id:guid}", (Guid id, UpdateBranchRequest request) =>
{
    if (!branches.TryGetValue(id, out var branch))
    {
        return HttpResults.NotFound("Branch", id);
    }

    var updated = branch with
    {
        Name = request.Name ?? branch.Name,
        Address = request.Address ?? branch.Address,
        Phone = request.Phone ?? branch.Phone,
        Timezone = request.Timezone ?? branch.Timezone,
        IsActive = request.IsActive ?? branch.IsActive
    };
    branches[id] = updated;
    return Results.Ok(updated);
});

app.MapGet("/api/branches/{id:guid}/working-hours", (Guid id) =>
{
    if (!branches.ContainsKey(id))
    {
        return HttpResults.NotFound("Branch", id);
    }

    return Results.Ok(workingHours.Values.Where(hours => hours.BranchId == id).OrderBy(hours => hours.DayOfWeek));
});

app.MapPut("/api/branches/{id:guid}/working-hours", (Guid id, IReadOnlyCollection<UpdateBranchWorkingHoursRequest> request) =>
{
    if (!branches.ContainsKey(id))
    {
        return HttpResults.NotFound("Branch", id);
    }

    foreach (var item in request)
    {
        workingHours[(id, item.DayOfWeek)] = new BranchWorkingHours(id, item.DayOfWeek, item.OpensAt, item.ClosesAt, item.IsClosed);
    }

    return Results.Ok(workingHours.Values.Where(hours => hours.BranchId == id).OrderBy(hours => hours.DayOfWeek));
});

app.MapGet("/api/branches/{id:guid}/halls", (Guid id) =>
    Results.Ok(halls.Values.Where(hall => hall.BranchId == id).OrderBy(hall => hall.Name)));

app.MapGet("/api/branches/{id:guid}/zones", (Guid id) =>
{
    if (!branches.ContainsKey(id))
    {
        return HttpResults.NotFound("Branch", id);
    }

    return Results.Ok(zones.Values.Where(zone => zone.BranchId == id).OrderBy(zone => zone.Name));
});

app.MapGet("/api/branches/{id:guid}/floor-plan", (Guid id) =>
{
    if (!branches.ContainsKey(id))
    {
        return HttpResults.NotFound("Branch", id);
    }

    var branchHalls = halls.Values.Where(hall => hall.BranchId == id).OrderBy(hall => hall.Name).ToArray();
    var hallIds = branchHalls.Select(hall => hall.Id).ToHashSet();

    return Results.Ok(new FloorPlan(
        id,
        branchHalls,
        zones.Values.Where(zone => zone.BranchId == id && zone.IsActive).OrderBy(zone => zone.Name).ToArray(),
        tables.Values.Where(table => hallIds.Contains(table.HallId) && table.IsActive).OrderBy(table => table.Name).ToArray()));
});

app.MapPost("/api/zones", (CreateZoneRequest request) =>
{
    if (!branches.ContainsKey(request.BranchId))
    {
        return HttpResults.NotFound("Branch", request.BranchId);
    }

    var zone = new Zone(Guid.NewGuid(), request.BranchId, request.Name, request.Description, request.Color, true);
    zones[zone.Id] = zone;
    return Results.Created($"/api/zones/{zone.Id}", zone);
});

app.MapPost("/api/halls", (CreateHallRequest request) =>
{
    if (!branches.ContainsKey(request.BranchId))
    {
        return HttpResults.NotFound("Branch", request.BranchId);
    }

    var hall = new Hall(Guid.NewGuid(), request.BranchId, request.Name, request.Description);
    halls[hall.Id] = hall;
    return Results.Created($"/api/halls/{hall.Id}", hall);
});

app.MapGet("/api/halls/{id:guid}/tables", (Guid id) =>
    Results.Ok(tables.Values.Where(table => table.HallId == id).OrderBy(table => table.Name)));

app.MapGet("/api/tables/{id:guid}", (Guid id) =>
    tables.TryGetValue(id, out var table) ? Results.Ok(table) : HttpResults.NotFound("Table", id));

app.MapPost("/api/tables", (CreateTableRequest request) =>
{
    if (!halls.ContainsKey(request.HallId))
    {
        return HttpResults.NotFound("Hall", request.HallId);
    }

    if (request.ZoneId is not null && !zones.ContainsKey(request.ZoneId.Value))
    {
        return HttpResults.NotFound("Zone", request.ZoneId.Value);
    }

    var table = new TableSeat(Guid.NewGuid(), request.HallId, request.ZoneId, request.Name, request.Capacity, "FREE", request.XPosition, request.YPosition, true);
    tables[table.Id] = table;
    return Results.Created($"/api/tables/{table.Id}", table);
});

app.MapPatch("/api/tables/{id:guid}/status", (Guid id, UpdateTableStatusRequest request) =>
{
    if (!tables.TryGetValue(id, out var table))
    {
        return HttpResults.NotFound("Table", id);
    }

    tables[id] = table with { Status = request.Status };
    return Results.Ok(tables[id]);
});

app.MapGet("/api/hookahs", (Guid? branchId, string? status) =>
{
    var query = hookahs.Values.AsEnumerable();
    if (branchId is not null)
    {
        query = query.Where(hookah => hookah.BranchId == branchId);
    }
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(hookah => string.Equals(hookah.Status, status, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.OrderBy(hookah => hookah.Name));
});

app.MapGet("/api/hookahs/{id:guid}", (Guid id) =>
    hookahs.TryGetValue(id, out var hookah) ? Results.Ok(hookah) : HttpResults.NotFound("Hookah", id));

app.MapPost("/api/hookahs", (CreateHookahRequest request) =>
{
    if (!branches.ContainsKey(request.BranchId))
    {
        return HttpResults.NotFound("Branch", request.BranchId);
    }

    var hookah = new Hookah(Guid.NewGuid(), request.BranchId, request.Name, request.Brand, request.Model, request.Status, request.PhotoUrl, null);
    hookahs[hookah.Id] = hookah;
    return Results.Created($"/api/hookahs/{hookah.Id}", hookah);
});

app.MapPatch("/api/hookahs/{id:guid}/status", (Guid id, UpdateHookahStatusRequest request) =>
{
    if (!hookahs.TryGetValue(id, out var hookah))
    {
        return HttpResults.NotFound("Hookah", id);
    }

    hookahs[id] = hookah with { Status = request.Status };
    return Results.Ok(hookahs[id]);
});

app.Run();

static void SeedBranches(IDictionary<Guid, Branch> branches, IDictionary<Guid, Hall> halls, IDictionary<Guid, Zone> zones, IDictionary<Guid, TableSeat> tables, IDictionary<Guid, Hookah> hookahs, IDictionary<(Guid BranchId, DayOfWeek DayOfWeek), BranchWorkingHours> workingHours)
{
    var branchId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var hallId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    var zoneId = Guid.Parse("21000000-0000-0000-0000-000000000001");
    branches[branchId] = new Branch(branchId, "Hookah Place Center", "Lenina, 1", "+79990000000", "Europe/Moscow", true, DateTimeOffset.UtcNow);
    halls[hallId] = new Hall(hallId, branchId, "Main hall", "First floor");
    zones[zoneId] = new Zone(zoneId, branchId, "Main zone", "Central seating area", "#2f7d6d", true);
    tables[Guid.Parse("30000000-0000-0000-0000-000000000001")] = new TableSeat(Guid.Parse("30000000-0000-0000-0000-000000000001"), hallId, zoneId, "Table 1", 4, "FREE", 120, 300, true);
    tables[Guid.Parse("30000000-0000-0000-0000-000000000002")] = new TableSeat(Guid.Parse("30000000-0000-0000-0000-000000000002"), hallId, zoneId, "Table 2", 6, "FREE", 260, 300, true);
    hookahs[Guid.Parse("40000000-0000-0000-0000-000000000001")] = new Hookah(Guid.Parse("40000000-0000-0000-0000-000000000001"), branchId, "Alpha X", "Alpha Hookah", "X", HookahStatuses.Available, null, null);

    foreach (var day in Enum.GetValues<DayOfWeek>())
    {
        workingHours[(branchId, day)] = new BranchWorkingHours(branchId, day, new TimeOnly(12, 0), new TimeOnly(2, 0), false);
    }
}

public sealed record Branch(Guid Id, string Name, string Address, string Phone, string Timezone, bool IsActive, DateTimeOffset CreatedAt);
public sealed record Hall(Guid Id, Guid BranchId, string Name, string? Description);
public sealed record Zone(Guid Id, Guid BranchId, string Name, string? Description, string? Color, bool IsActive);
public sealed record TableSeat(Guid Id, Guid HallId, Guid? ZoneId, string Name, int Capacity, string Status, decimal XPosition, decimal YPosition, bool IsActive);
public sealed record Hookah(Guid Id, Guid BranchId, string Name, string Brand, string Model, string Status, string? PhotoUrl, DateTimeOffset? LastServiceAt);
public sealed record BranchWorkingHours(Guid BranchId, DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, bool IsClosed);
public sealed record FloorPlan(Guid BranchId, IReadOnlyCollection<Hall> Halls, IReadOnlyCollection<Zone> Zones, IReadOnlyCollection<TableSeat> Tables);
public sealed record CreateBranchRequest(string Name, string Address, string Phone, string Timezone);
public sealed record UpdateBranchRequest(string? Name, string? Address, string? Phone, string? Timezone, bool? IsActive);
public sealed record UpdateBranchWorkingHoursRequest(DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, bool IsClosed);
public sealed record CreateZoneRequest(Guid BranchId, string Name, string? Description, string? Color);
public sealed record CreateHallRequest(Guid BranchId, string Name, string? Description);
public sealed record CreateTableRequest(Guid HallId, Guid? ZoneId, string Name, int Capacity, decimal XPosition, decimal YPosition);
public sealed record UpdateTableStatusRequest(string Status);
public sealed record CreateHookahRequest(Guid BranchId, string Name, string Brand, string Model, string Status, string? PhotoUrl);
public sealed record UpdateHookahStatusRequest(string Status);
