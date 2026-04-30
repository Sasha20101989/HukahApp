using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("booking-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var tables = new Dictionary<Guid, BookingTable>
{
    [Guid.Parse("30000000-0000-0000-0000-000000000001")] = new(Guid.Parse("30000000-0000-0000-0000-000000000001"), Guid.Parse("10000000-0000-0000-0000-000000000001"), 4, true),
    [Guid.Parse("30000000-0000-0000-0000-000000000002")] = new(Guid.Parse("30000000-0000-0000-0000-000000000002"), Guid.Parse("10000000-0000-0000-0000-000000000001"), 6, true)
};
var bookings = new Dictionary<Guid, Booking>();

app.MapGet("/api/bookings/availability", (Guid branchId, DateOnly date, TimeOnly time, int guestsCount) =>
{
    var start = new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero);
    var end = start.AddHours(2);
    var available = tables.Values
        .Where(table => table.BranchId == branchId && table.IsActive && table.Capacity >= guestsCount)
        .Where(table => bookings.Values.All(booking => booking.TableId != table.Id || booking.Status is "CANCELLED" or "NO_SHOW" || !DomainRules.Intersects(start, end, booking.StartTime, booking.EndTime)))
        .OrderBy(table => table.Capacity);

    return Results.Ok(available);
});

app.MapPost("/api/bookings", async (CreateBookingRequest request, IEventPublisher events) =>
{
    if (!tables.TryGetValue(request.TableId, out var table) || table.BranchId != request.BranchId)
    {
        return HttpResults.NotFound("Table", request.TableId);
    }

    if (table.Capacity < request.GuestsCount)
    {
        return HttpResults.Validation("Table capacity is less than guests count.");
    }

    var intersects = bookings.Values.Any(booking =>
        booking.TableId == request.TableId &&
        booking.Status is not "CANCELLED" and not "NO_SHOW" &&
        DomainRules.Intersects(request.StartTime, request.EndTime, booking.StartTime, booking.EndTime));

    if (intersects)
    {
        return HttpResults.Conflict("Booking time intersects with an existing booking.");
    }

    var status = request.DepositAmount > 0 ? "WAITING_PAYMENT" : "CONFIRMED";
    var booking = new Booking(Guid.NewGuid(), request.ClientId, request.BranchId, request.TableId, request.HookahId, request.BowlId, request.MixId, request.StartTime, request.EndTime, request.GuestsCount, status, request.DepositAmount, request.Comment, DateTimeOffset.UtcNow);
    bookings[booking.Id] = booking;

    await events.PublishAsync(new BookingCreated(booking.Id, booking.BranchId, booking.TableId, booking.ClientId, booking.StartTime, booking.EndTime, DateTimeOffset.UtcNow));
    if (status == "CONFIRMED")
    {
        await events.PublishAsync(new BookingConfirmed(booking.Id, DateTimeOffset.UtcNow));
    }

    return Results.Created($"/api/bookings/{booking.Id}", booking);
});

app.MapGet("/api/bookings", (Guid? branchId, DateOnly? date, string? status) =>
{
    var query = bookings.Values.AsEnumerable();
    if (branchId is not null)
    {
        query = query.Where(booking => booking.BranchId == branchId);
    }
    if (date is not null)
    {
        query = query.Where(booking => DateOnly.FromDateTime(booking.StartTime.UtcDateTime) == date);
    }
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(booking => string.Equals(booking.Status, status, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.OrderBy(booking => booking.StartTime));
});

app.MapGet("/api/bookings/{id:guid}", (Guid id) =>
    bookings.TryGetValue(id, out var booking) ? Results.Ok(booking) : HttpResults.NotFound("Booking", id));

app.MapPatch("/api/bookings/{id:guid}/confirm", async (Guid id, IEventPublisher events) =>
{
    if (!bookings.TryGetValue(id, out var booking))
    {
        return HttpResults.NotFound("Booking", id);
    }

    bookings[id] = booking with { Status = "CONFIRMED" };
    await events.PublishAsync(new BookingConfirmed(id, DateTimeOffset.UtcNow));
    return Results.Ok(bookings[id]);
});

app.MapPatch("/api/bookings/{id:guid}/cancel", async (Guid id, CancelBookingRequest request, IEventPublisher events) =>
{
    if (!bookings.TryGetValue(id, out var booking))
    {
        return HttpResults.NotFound("Booking", id);
    }

    bookings[id] = booking with { Status = "CANCELLED" };
    await events.PublishAsync(new BookingCancelled(id, request.Reason, DateTimeOffset.UtcNow));
    return Results.Ok(bookings[id]);
});

app.MapPatch("/api/bookings/{id:guid}/reschedule", (Guid id, RescheduleBookingRequest request) =>
{
    if (!bookings.TryGetValue(id, out var booking))
    {
        return HttpResults.NotFound("Booking", id);
    }

    var intersects = bookings.Values.Any(existing =>
        existing.Id != id &&
        existing.TableId == request.TableId &&
        existing.Status is not "CANCELLED" and not "NO_SHOW" &&
        DomainRules.Intersects(request.StartTime, request.EndTime, existing.StartTime, existing.EndTime));

    if (intersects)
    {
        return HttpResults.Conflict("Reschedule time intersects with an existing booking.");
    }

    bookings[id] = booking with { StartTime = request.StartTime, EndTime = request.EndTime, TableId = request.TableId };
    return Results.Ok(bookings[id]);
});

app.MapPatch("/api/bookings/{id:guid}/no-show", (Guid id) =>
{
    if (!bookings.TryGetValue(id, out var booking))
    {
        return HttpResults.NotFound("Booking", id);
    }

    bookings[id] = booking with { Status = "NO_SHOW" };
    return Results.Ok(bookings[id]);
});

app.Run();

public sealed record BookingTable(Guid Id, Guid BranchId, int Capacity, bool IsActive);
public sealed record Booking(Guid Id, Guid ClientId, Guid BranchId, Guid TableId, Guid? HookahId, Guid? BowlId, Guid? MixId, DateTimeOffset StartTime, DateTimeOffset EndTime, int GuestsCount, string Status, decimal DepositAmount, string? Comment, DateTimeOffset CreatedAt);
public sealed record CreateBookingRequest(Guid BranchId, Guid TableId, Guid ClientId, DateTimeOffset StartTime, DateTimeOffset EndTime, int GuestsCount, Guid? HookahId, Guid? BowlId, Guid? MixId, string? Comment, decimal DepositAmount);
public sealed record CancelBookingRequest(string Reason);
public sealed record RescheduleBookingRequest(DateTimeOffset StartTime, DateTimeOffset EndTime, Guid TableId);
