using HookahPlatform.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("branch-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var branches = new Dictionary<Guid, Branch>();
var halls = new Dictionary<Guid, Hall>();
var tables = new Dictionary<Guid, TableSeat>();
var hookahs = new Dictionary<Guid, Hookah>();
SeedBranches(branches, halls, tables, hookahs);

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

app.MapGet("/api/branches/{id:guid}/halls", (Guid id) =>
    Results.Ok(halls.Values.Where(hall => hall.BranchId == id).OrderBy(hall => hall.Name)));

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

app.MapPost("/api/tables", (CreateTableRequest request) =>
{
    if (!halls.ContainsKey(request.HallId))
    {
        return HttpResults.NotFound("Hall", request.HallId);
    }

    var table = new TableSeat(Guid.NewGuid(), request.HallId, request.Name, request.Capacity, "FREE", request.XPosition, request.YPosition, true);
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

static void SeedBranches(IDictionary<Guid, Branch> branches, IDictionary<Guid, Hall> halls, IDictionary<Guid, TableSeat> tables, IDictionary<Guid, Hookah> hookahs)
{
    var branchId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var hallId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    branches[branchId] = new Branch(branchId, "Hookah Place Center", "Lenina, 1", "+79990000000", "Europe/Moscow", true, DateTimeOffset.UtcNow);
    halls[hallId] = new Hall(hallId, branchId, "Main hall", "First floor");
    tables[Guid.Parse("30000000-0000-0000-0000-000000000001")] = new TableSeat(Guid.Parse("30000000-0000-0000-0000-000000000001"), hallId, "Table 1", 4, "FREE", 120, 300, true);
    tables[Guid.Parse("30000000-0000-0000-0000-000000000002")] = new TableSeat(Guid.Parse("30000000-0000-0000-0000-000000000002"), hallId, "Table 2", 6, "FREE", 260, 300, true);
    hookahs[Guid.Parse("40000000-0000-0000-0000-000000000001")] = new Hookah(Guid.Parse("40000000-0000-0000-0000-000000000001"), branchId, "Alpha X", "Alpha Hookah", "X", "AVAILABLE", null, null);
}

public sealed record Branch(Guid Id, string Name, string Address, string Phone, string Timezone, bool IsActive, DateTimeOffset CreatedAt);
public sealed record Hall(Guid Id, Guid BranchId, string Name, string? Description);
public sealed record TableSeat(Guid Id, Guid HallId, string Name, int Capacity, string Status, decimal XPosition, decimal YPosition, bool IsActive);
public sealed record Hookah(Guid Id, Guid BranchId, string Name, string Brand, string Model, string Status, string? PhotoUrl, DateTimeOffset? LastServiceAt);
public sealed record CreateBranchRequest(string Name, string Address, string Phone, string Timezone);
public sealed record UpdateBranchRequest(string? Name, string? Address, string? Phone, string? Timezone, bool? IsActive);
public sealed record CreateHallRequest(Guid BranchId, string Name, string? Description);
public sealed record CreateTableRequest(Guid HallId, string Name, int Capacity, decimal XPosition, decimal YPosition);
public sealed record UpdateTableStatusRequest(string Status);
public sealed record CreateHookahRequest(Guid BranchId, string Name, string Brand, string Model, string Status, string? PhotoUrl);
public sealed record UpdateHookahStatusRequest(string Status);
