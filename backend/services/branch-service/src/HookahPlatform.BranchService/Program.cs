using HookahPlatform.BranchService.Persistence;
using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("branch-service");
builder.AddPostgresDbContext<BranchDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<BranchDbContext>("branch-service");

app.MapGet("/api/branches", async (BranchDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Branches.AsNoTracking().OrderBy(branch => branch.Name).ToListAsync(cancellationToken)));

app.MapPost("/api/branches", async (CreateBranchRequest request, BranchDbContext db, CancellationToken cancellationToken) =>
{
    var branch = new BranchEntity { Id = Guid.NewGuid(), Name = request.Name, Address = request.Address, Phone = request.Phone, Timezone = request.Timezone, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
    db.Branches.Add(branch);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/branches/{branch.Id}", branch);
});

app.MapGet("/api/branches/{id:guid}", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
    await db.Branches.AsNoTracking().FirstOrDefaultAsync(branch => branch.Id == id, cancellationToken) is { } branch
        ? Results.Ok(branch)
        : HttpResults.NotFound("Branch", id));

app.MapPatch("/api/branches/{id:guid}", async (Guid id, UpdateBranchRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var branch = await db.Branches.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (branch is null) return HttpResults.NotFound("Branch", id);
    branch.Name = request.Name ?? branch.Name;
    branch.Address = request.Address ?? branch.Address;
    branch.Phone = request.Phone ?? branch.Phone;
    branch.Timezone = request.Timezone ?? branch.Timezone;
    branch.IsActive = request.IsActive ?? branch.IsActive;
    await db.SaveChangesAsync(cancellationToken);
    await InvalidateBranchBookingCacheAsync(cache, id, cancellationToken);
    return Results.Ok(branch);
});

app.MapGet("/api/branches/{id:guid}/working-hours", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
{
    if (!await db.Branches.AnyAsync(branch => branch.Id == id, cancellationToken)) return HttpResults.NotFound("Branch", id);
    return Results.Ok(await db.WorkingHours.AsNoTracking().Where(hours => hours.BranchId == id).OrderBy(hours => hours.DayOfWeek).ToListAsync(cancellationToken));
});

app.MapPut("/api/branches/{id:guid}/working-hours", async (Guid id, IReadOnlyCollection<UpdateBranchWorkingHoursRequest> request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (!await db.Branches.AnyAsync(branch => branch.Id == id, cancellationToken)) return HttpResults.NotFound("Branch", id);
    var existing = await db.WorkingHours.Where(hours => hours.BranchId == id).ToListAsync(cancellationToken);
    db.WorkingHours.RemoveRange(existing);
    foreach (var item in request)
    {
        db.WorkingHours.Add(new BranchWorkingHoursEntity { BranchId = id, DayOfWeek = (int)item.DayOfWeek, OpensAt = item.OpensAt, ClosesAt = item.ClosesAt, IsClosed = item.IsClosed });
    }
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchWorkingHoursCacheKey(id), cancellationToken);
    return Results.Ok(await db.WorkingHours.AsNoTracking().Where(hours => hours.BranchId == id).OrderBy(hours => hours.DayOfWeek).ToListAsync(cancellationToken));
});

app.MapGet("/api/branches/{id:guid}/halls", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Halls.AsNoTracking().Where(hall => hall.BranchId == id).OrderBy(hall => hall.Name).ToListAsync(cancellationToken)));

app.MapGet("/api/branches/{id:guid}/zones", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
{
    if (!await db.Branches.AnyAsync(branch => branch.Id == id, cancellationToken)) return HttpResults.NotFound("Branch", id);
    return Results.Ok(await db.Zones.AsNoTracking().Where(zone => zone.BranchId == id).OrderBy(zone => zone.Name).ToListAsync(cancellationToken));
});

app.MapGet("/api/branches/{id:guid}/floor-plan", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
{
    if (!await db.Branches.AnyAsync(branch => branch.Id == id, cancellationToken)) return HttpResults.NotFound("Branch", id);
    var branchHalls = await db.Halls.AsNoTracking().Where(hall => hall.BranchId == id).OrderBy(hall => hall.Name).ToListAsync(cancellationToken);
    var hallIds = branchHalls.Select(hall => hall.Id).ToArray();
    var branchZones = await db.Zones.AsNoTracking().Where(zone => zone.BranchId == id && zone.IsActive).OrderBy(zone => zone.Name).ToListAsync(cancellationToken);
    var branchTables = await db.Tables.AsNoTracking().Where(table => hallIds.Contains(table.HallId) && table.IsActive).OrderBy(table => table.Name).ToListAsync(cancellationToken);
    return Results.Ok(new FloorPlan(id, branchHalls, branchZones, branchTables));
});

app.MapPost("/api/zones", async (CreateZoneRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (!await db.Branches.AnyAsync(branch => branch.Id == request.BranchId, cancellationToken)) return HttpResults.NotFound("Branch", request.BranchId);
    var zone = new ZoneEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, Name = request.Name, Description = request.Description, Color = request.Color, XPosition = request.XPosition ?? 40, YPosition = request.YPosition ?? 40, Width = request.Width ?? 360, Height = request.Height ?? 220, IsActive = true };
    db.Zones.Add(zone);
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(request.BranchId), cancellationToken);
    return Results.Created($"/api/zones/{zone.Id}", zone);
});

app.MapPatch("/api/zones/{id:guid}", async (Guid id, UpdateZoneRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var zone = await db.Zones.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (zone is null) return HttpResults.NotFound("Zone", id);
    zone.Name = request.Name ?? zone.Name;
    zone.Description = request.Description ?? zone.Description;
    zone.Color = request.Color ?? zone.Color;
    zone.XPosition = request.XPosition ?? zone.XPosition;
    zone.YPosition = request.YPosition ?? zone.YPosition;
    zone.Width = request.Width ?? zone.Width;
    zone.Height = request.Height ?? zone.Height;
    zone.IsActive = request.IsActive ?? zone.IsActive;
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(zone.BranchId), cancellationToken);
    return Results.Ok(zone);
});

app.MapDelete("/api/zones/{id:guid}", async (Guid id, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var zone = await db.Zones.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (zone is null) return HttpResults.NotFound("Zone", id);
    zone.IsActive = false;
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(zone.BranchId), cancellationToken);
    return Results.NoContent();
});

app.MapPost("/api/halls", async (CreateHallRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (!await db.Branches.AnyAsync(branch => branch.Id == request.BranchId, cancellationToken)) return HttpResults.NotFound("Branch", request.BranchId);
    var hall = new HallEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, Name = request.Name, Description = request.Description };
    db.Halls.Add(hall);
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(request.BranchId), cancellationToken);
    return Results.Created($"/api/halls/{hall.Id}", hall);
});

app.MapPatch("/api/halls/{id:guid}", async (Guid id, UpdateHallRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var hall = await db.Halls.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (hall is null) return HttpResults.NotFound("Hall", id);
    hall.Name = request.Name ?? hall.Name;
    hall.Description = request.Description ?? hall.Description;
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(hall.BranchId), cancellationToken);
    return Results.Ok(hall);
});

app.MapDelete("/api/halls/{id:guid}", async (Guid id, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var hall = await db.Halls.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (hall is null) return HttpResults.NotFound("Hall", id);
    if (await db.Tables.AnyAsync(table => table.HallId == id && table.IsActive, cancellationToken)) return HttpResults.Conflict("Hall has active tables.");
    db.Halls.Remove(hall);
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(hall.BranchId), cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/halls/{id:guid}/tables", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Tables.AsNoTracking().Where(table => table.HallId == id).OrderBy(table => table.Name).ToListAsync(cancellationToken)));

app.MapGet("/api/tables/{id:guid}", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
    await db.Tables.AsNoTracking().FirstOrDefaultAsync(table => table.Id == id, cancellationToken) is { } table
        ? Results.Ok(table)
        : HttpResults.NotFound("Table", id));

app.MapPost("/api/tables", async (CreateTableRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var hall = await db.Halls.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == request.HallId, cancellationToken);
    if (hall is null) return HttpResults.NotFound("Hall", request.HallId);
    if (request.ZoneId is not null && !await db.Zones.AnyAsync(zone => zone.Id == request.ZoneId.Value, cancellationToken)) return HttpResults.NotFound("Zone", request.ZoneId.Value);
    var table = new TableEntity { Id = Guid.NewGuid(), HallId = request.HallId, ZoneId = request.ZoneId, Name = request.Name, Capacity = request.Capacity, Status = "FREE", XPosition = request.XPosition, YPosition = request.YPosition, IsActive = true };
    db.Tables.Add(table);
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(hall.BranchId), cancellationToken);
    return Results.Created($"/api/tables/{table.Id}", table);
});

app.MapPatch("/api/tables/{id:guid}", async (Guid id, UpdateTableRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var table = await db.Tables.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (table is null) return HttpResults.NotFound("Table", id);
    if (request.HallId is not null && !await db.Halls.AnyAsync(hall => hall.Id == request.HallId.Value, cancellationToken)) return HttpResults.NotFound("Hall", request.HallId.Value);
    if (request.ZoneId is not null && !await db.Zones.AnyAsync(zone => zone.Id == request.ZoneId.Value, cancellationToken)) return HttpResults.NotFound("Zone", request.ZoneId.Value);
    var oldHall = await db.Halls.AsNoTracking().FirstAsync(candidate => candidate.Id == table.HallId, cancellationToken);
    table.HallId = request.HallId ?? table.HallId;
    table.ZoneId = request.ZoneId ?? table.ZoneId;
    table.Name = request.Name ?? table.Name;
    table.Capacity = request.Capacity ?? table.Capacity;
    table.Status = request.Status ?? table.Status;
    table.XPosition = request.XPosition ?? table.XPosition;
    table.YPosition = request.YPosition ?? table.YPosition;
    table.IsActive = request.IsActive ?? table.IsActive;
    var newHall = await db.Halls.AsNoTracking().FirstAsync(candidate => candidate.Id == table.HallId, cancellationToken);
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(oldHall.BranchId), cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(newHall.BranchId), cancellationToken);
    return Results.Ok(table);
});

app.MapPatch("/api/tables/{id:guid}/status", async (Guid id, UpdateTableStatusRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var table = await db.Tables.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (table is null) return HttpResults.NotFound("Table", id);
    var hall = await db.Halls.AsNoTracking().FirstAsync(candidate => candidate.Id == table.HallId, cancellationToken);
    table.Status = request.Status;
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(hall.BranchId), cancellationToken);
    await cache.SetJsonAsync($"crm:branch:{hall.BranchId}:table:{table.Id}:state", new RuntimeResourceState(table.Id, "TABLE", request.Status, DateTimeOffset.UtcNow), TimeSpan.FromHours(12), cancellationToken);
    return Results.Ok(table);
});

app.MapDelete("/api/tables/{id:guid}", async (Guid id, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var table = await db.Tables.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (table is null) return HttpResults.NotFound("Table", id);
    var hall = await db.Halls.AsNoTracking().FirstAsync(candidate => candidate.Id == table.HallId, cancellationToken);
    table.IsActive = false;
    table.Status = "WRITTEN_OFF";
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(BranchTablesCacheKey(hall.BranchId), cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/hookahs", async (Guid? branchId, string? status, BranchDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Hookahs.AsNoTracking();
    if (branchId is not null) query = query.Where(hookah => hookah.BranchId == branchId);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(hookah => hookah.Status == status);
    return Results.Ok(await query.OrderBy(hookah => hookah.Name).ToListAsync(cancellationToken));
});

app.MapGet("/api/hookahs/{id:guid}", async (Guid id, BranchDbContext db, CancellationToken cancellationToken) =>
    await db.Hookahs.AsNoTracking().FirstOrDefaultAsync(hookah => hookah.Id == id, cancellationToken) is { } hookah
        ? Results.Ok(hookah)
        : HttpResults.NotFound("Hookah", id));

app.MapPost("/api/hookahs", async (CreateHookahRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (!await db.Branches.AnyAsync(branch => branch.Id == request.BranchId, cancellationToken)) return HttpResults.NotFound("Branch", request.BranchId);
    var hookah = new HookahEntity { Id = Guid.NewGuid(), BranchId = request.BranchId, Name = request.Name, Brand = request.Brand, Model = request.Model, Status = request.Status, PhotoUrl = request.PhotoUrl, LastServiceAt = null };
    db.Hookahs.Add(hookah);
    await db.SaveChangesAsync(cancellationToken);
    await cache.SetJsonAsync($"crm:branch:{hookah.BranchId}:hookah:{hookah.Id}:state", new RuntimeResourceState(hookah.Id, "HOOKAH", hookah.Status, DateTimeOffset.UtcNow), TimeSpan.FromHours(12), cancellationToken);
    return Results.Created($"/api/hookahs/{hookah.Id}", hookah);
});

app.MapPatch("/api/hookahs/{id:guid}", async (Guid id, UpdateHookahRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var hookah = await db.Hookahs.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (hookah is null) return HttpResults.NotFound("Hookah", id);
    hookah.Name = request.Name ?? hookah.Name;
    hookah.Brand = request.Brand ?? hookah.Brand;
    hookah.Model = request.Model ?? hookah.Model;
    hookah.Status = request.Status ?? hookah.Status;
    hookah.PhotoUrl = request.PhotoUrl ?? hookah.PhotoUrl;
    hookah.LastServiceAt = request.LastServiceAt ?? hookah.LastServiceAt;
    await db.SaveChangesAsync(cancellationToken);
    await cache.SetJsonAsync($"crm:branch:{hookah.BranchId}:hookah:{hookah.Id}:state", new RuntimeResourceState(hookah.Id, "HOOKAH", hookah.Status, DateTimeOffset.UtcNow), TimeSpan.FromHours(12), cancellationToken);
    return Results.Ok(hookah);
});

app.MapPatch("/api/hookahs/{id:guid}/status", async (Guid id, UpdateHookahStatusRequest request, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var hookah = await db.Hookahs.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (hookah is null) return HttpResults.NotFound("Hookah", id);
    hookah.Status = request.Status;
    await db.SaveChangesAsync(cancellationToken);
    await cache.SetJsonAsync($"crm:branch:{hookah.BranchId}:hookah:{hookah.Id}:state", new RuntimeResourceState(hookah.Id, "HOOKAH", hookah.Status, DateTimeOffset.UtcNow), TimeSpan.FromHours(12), cancellationToken);
    return Results.Ok(hookah);
});

app.MapDelete("/api/hookahs/{id:guid}", async (Guid id, BranchDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var hookah = await db.Hookahs.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (hookah is null) return HttpResults.NotFound("Hookah", id);
    hookah.Status = "WRITTEN_OFF";
    await db.SaveChangesAsync(cancellationToken);
    await cache.SetJsonAsync($"crm:branch:{hookah.BranchId}:hookah:{hookah.Id}:state", new RuntimeResourceState(hookah.Id, "HOOKAH", hookah.Status, DateTimeOffset.UtcNow), TimeSpan.FromHours(12), cancellationToken);
    return Results.NoContent();
});

app.Run();

static string BranchTablesCacheKey(Guid branchId) => $"booking:branch:{branchId}:tables";
static string BranchWorkingHoursCacheKey(Guid branchId) => $"booking:branch:{branchId}:working-hours";

static async Task InvalidateBranchBookingCacheAsync(IDistributedCache cache, Guid branchId, CancellationToken cancellationToken)
{
    await cache.RemoveAsync(BranchTablesCacheKey(branchId), cancellationToken);
    await cache.RemoveAsync(BranchWorkingHoursCacheKey(branchId), cancellationToken);
}

public sealed record FloorPlan(Guid BranchId, IReadOnlyCollection<HallEntity> Halls, IReadOnlyCollection<ZoneEntity> Zones, IReadOnlyCollection<TableEntity> Tables);
public sealed record CreateBranchRequest(string Name, string Address, string Phone, string Timezone);
public sealed record UpdateBranchRequest(string? Name, string? Address, string? Phone, string? Timezone, bool? IsActive);
public sealed record UpdateBranchWorkingHoursRequest(DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, bool IsClosed);
public sealed record CreateZoneRequest(Guid BranchId, string Name, string? Description, string? Color, decimal? XPosition, decimal? YPosition, decimal? Width, decimal? Height);
public sealed record UpdateZoneRequest(string? Name, string? Description, string? Color, decimal? XPosition, decimal? YPosition, decimal? Width, decimal? Height, bool? IsActive);
public sealed record CreateHallRequest(Guid BranchId, string Name, string? Description);
public sealed record UpdateHallRequest(string? Name, string? Description);
public sealed record CreateTableRequest(Guid HallId, Guid? ZoneId, string Name, int Capacity, decimal XPosition, decimal YPosition);
public sealed record UpdateTableRequest(Guid? HallId, Guid? ZoneId, string? Name, int? Capacity, string? Status, decimal? XPosition, decimal? YPosition, bool? IsActive);
public sealed record UpdateTableStatusRequest(string Status);
public sealed record CreateHookahRequest(Guid BranchId, string Name, string Brand, string Model, string Status, string? PhotoUrl);
public sealed record UpdateHookahRequest(string? Name, string? Brand, string? Model, string? Status, string? PhotoUrl, DateTimeOffset? LastServiceAt);
public sealed record UpdateHookahStatusRequest(string Status);
public sealed record RuntimeResourceState(Guid ResourceId, string ResourceType, string Status, DateTimeOffset UpdatedAt);
