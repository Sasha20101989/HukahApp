using HookahPlatform.BookingService.Persistence;
using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("booking-service");
builder.AddPostgresDbContext<BookingDbContext>();
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<BookingDbContext>("booking-service");

var allowedBookingStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    BookingStatuses.New,
    BookingStatuses.WaitingPayment,
    BookingStatuses.Paid,
    BookingStatuses.Confirmed,
    BookingStatuses.ClientArrived,
    BookingStatuses.Completed,
    BookingStatuses.Cancelled,
    BookingStatuses.NoShow
};

app.MapGet("/api/bookings/availability", async (Guid branchId, DateOnly date, TimeOnly time, int guestsCount, BookingDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var start = new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero);
    var end = start.AddHours(2);
    var workingHours = await GetBranchWorkingHoursAsync(branchId, httpClientFactory, configuration, cancellationToken);
    if (!BranchIsOpen(branchId, start, end, workingHours)) return Results.Ok(Array.Empty<BookingTable>());

    var tables = await GetBranchTablesAsync(branchId, httpClientFactory, configuration, cancellationToken);

    var branchBookings = await db.Bookings.AsNoTracking().Where(booking => booking.BranchId == branchId && booking.Status != BookingStatuses.Cancelled && booking.Status != BookingStatuses.NoShow).ToListAsync(cancellationToken);
    var available = tables
        .Where(table => table.BranchId == branchId && table.IsActive && table.Capacity >= guestsCount)
        .Where(table => branchBookings.All(booking => booking.TableId != table.Id || !DomainRules.Intersects(start, end, booking.StartTime, booking.EndTime)))
        .OrderBy(table => table.Capacity);

    return Results.Ok(available);
});

app.MapPost("/api/bookings", async (CreateBookingRequest request, BookingDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var table = (await GetBranchTablesAsync(request.BranchId, httpClientFactory, configuration, cancellationToken)).FirstOrDefault(candidate => candidate.Id == request.TableId);
    if (table is null) return HttpResults.NotFound("Table", request.TableId);
    if (table.Capacity < request.GuestsCount) return HttpResults.Validation("Table capacity is less than guests count.");
    var workingHours = await GetBranchWorkingHoursAsync(request.BranchId, httpClientFactory, configuration, cancellationToken);
    if (!BranchIsOpen(request.BranchId, request.StartTime, request.EndTime, workingHours)) return HttpResults.Validation("Branch is closed for the requested booking time.");

    var eligibility = await GetBookingEligibilityAsync(request.ClientId, httpClientFactory, configuration, cancellationToken);
    if (!eligibility.IsEligible) return HttpResults.Conflict(eligibility.Reason ?? "Client cannot create bookings.");

    var intersects = await db.Bookings.AnyAsync(booking =>
        booking.TableId == request.TableId &&
        booking.Status != BookingStatuses.Cancelled &&
        booking.Status != BookingStatuses.NoShow &&
        request.StartTime < booking.EndTime &&
        request.EndTime > booking.StartTime,
        cancellationToken);
    if (intersects) return HttpResults.Conflict("Booking time intersects with an existing booking.");

    var status = request.DepositAmount > 0 ? BookingStatuses.WaitingPayment : BookingStatuses.Confirmed;
    var booking = new BookingEntity { Id = Guid.NewGuid(), ClientId = request.ClientId, BranchId = request.BranchId, TableId = request.TableId, HookahId = request.HookahId, BowlId = request.BowlId, MixId = request.MixId, StartTime = request.StartTime, EndTime = request.EndTime, GuestsCount = request.GuestsCount, Status = status, DepositAmount = request.DepositAmount, PaymentId = null, DepositPaidAt = null, Comment = request.Comment, CreatedAt = DateTimeOffset.UtcNow };
    db.Bookings.Add(booking);
    var outboxEvents = new List<IIntegrationEvent>
    {
        new BookingCreated(booking.Id, booking.BranchId, booking.TableId, booking.ClientId, booking.StartTime, booking.EndTime, DateTimeOffset.UtcNow)
    };
    if (status == BookingStatuses.Confirmed) outboxEvents.Add(new BookingConfirmed(booking.Id, DateTimeOffset.UtcNow));
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);

    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);
    return Results.Created($"/api/bookings/{booking.Id}", booking);
});

app.MapGet("/api/bookings", async (Guid? branchId, DateOnly? date, string? status, BookingDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Bookings.AsNoTracking();
    if (branchId is not null) query = query.Where(booking => booking.BranchId == branchId);
    if (date is not null) query = query.Where(booking => DateOnly.FromDateTime(booking.StartTime.UtcDateTime) == date);
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!allowedBookingStatuses.Contains(status)) return HttpResults.Validation($"Unsupported booking status '{status}'.");
        query = query.Where(booking => booking.Status == status);
    }
    return Results.Ok(await query.OrderBy(booking => booking.StartTime).ToListAsync(cancellationToken));
});

app.MapGet("/api/bookings/{id:guid}", async (Guid id, BookingDbContext db, CancellationToken cancellationToken) =>
    await db.Bookings.AsNoTracking().FirstOrDefaultAsync(booking => booking.Id == id, cancellationToken) is { } booking
        ? Results.Ok(booking)
        : HttpResults.NotFound("Booking", id));

app.MapPatch("/api/bookings/{id:guid}/confirm", async (Guid id, BookingDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    if (booking.DepositAmount > 0 && booking.DepositPaidAt is null) return HttpResults.Conflict("Booking with required deposit cannot be confirmed before payment.");
    booking.Status = BookingStatuses.Confirmed;
    var confirmed = new BookingConfirmed(id, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(confirmed);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, confirmed, outboxMessage, cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/payment-succeeded", async (Guid id, BookingPaymentSucceededRequest request, BookingDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    if (request.Amount < booking.DepositAmount) return HttpResults.Validation("Payment amount is less than required deposit.");
    var paidAt = DateTimeOffset.UtcNow;
    booking.Status = BookingStatuses.Confirmed;
    booking.PaymentId = request.PaymentId;
    booking.DepositPaidAt = paidAt;
    var outboxEvents = new IIntegrationEvent[]
    {
        new BookingPaid(id, request.PaymentId, request.Amount, paidAt),
        new BookingConfirmed(id, DateTimeOffset.UtcNow)
    };
    var outboxMessages = db.AddOutboxMessages(outboxEvents);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/cancel", async (Guid id, CancelBookingRequest request, BookingDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    booking.Status = BookingStatuses.Cancelled;
    var cancelled = new BookingCancelled(id, request.Reason, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(cancelled);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, cancelled, outboxMessage, cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/reschedule", async (Guid id, RescheduleBookingRequest request, BookingDbContext db, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    var intersects = await db.Bookings.AnyAsync(existing => existing.Id != id && existing.TableId == request.TableId && existing.Status != BookingStatuses.Cancelled && existing.Status != BookingStatuses.NoShow && request.StartTime < existing.EndTime && request.EndTime > existing.StartTime, cancellationToken);
    if (intersects) return HttpResults.Conflict("Reschedule time intersects with an existing booking.");
    booking.StartTime = request.StartTime;
    booking.EndTime = request.EndTime;
    booking.TableId = request.TableId;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/no-show", async (Guid id, BookingDbContext db, CancellationToken cancellationToken) => await SetBookingStatusAsync(id, BookingStatuses.NoShow, db, cancellationToken));
app.MapPatch("/api/bookings/{id:guid}/client-arrived", async (Guid id, BookingDbContext db, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    if (booking.Status != BookingStatuses.Confirmed) return HttpResults.Conflict("Only confirmed bookings can be marked as client arrived.");
    booking.Status = BookingStatuses.ClientArrived;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(booking);
});
app.MapPatch("/api/bookings/{id:guid}/complete", async (Guid id, BookingDbContext db, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    if (booking.Status is not BookingStatuses.ClientArrived and not BookingStatuses.Confirmed) return HttpResults.Conflict("Only confirmed or arrived bookings can be completed.");
    booking.Status = BookingStatuses.Completed;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/mark-expired-no-shows", async (DateTimeOffset? now, BookingDbContext db, CancellationToken cancellationToken) =>
{
    var referenceTime = now ?? DateTimeOffset.UtcNow;
    var expired = await db.Bookings.Where(booking => booking.Status == BookingStatuses.Confirmed && booking.StartTime.AddMinutes(20) < referenceTime).ToListAsync(cancellationToken);
    foreach (var booking in expired) booking.Status = BookingStatuses.NoShow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(expired);
});

app.Run();

static async Task<IResult> SetBookingStatusAsync(Guid id, string status, BookingDbContext db, CancellationToken cancellationToken)
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    booking.Status = status;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(booking);
}

static async Task<BookingEligibility> GetBookingEligibilityAsync(Guid clientId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var userBaseUrl = configuration["Services:user-service:BaseUrl"] ?? "http://user-service:8080";
    var client = httpClientFactory.CreateClient("booking-service");
    var response = await client.GetAsync($"{userBaseUrl}/api/users/{clientId}/booking-eligibility", cancellationToken);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new BookingEligibility(clientId, false, "Client does not exist.");
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<BookingEligibility>(cancellationToken))!;
}

static async Task<IReadOnlyCollection<BookingTable>> GetBranchTablesAsync(Guid branchId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("booking-service");
    var floorPlan = await client.GetFromJsonAsync<FloorPlanDto>($"{branchBaseUrl}/api/branches/{branchId}/floor-plan", cancellationToken);
    return floorPlan?.Tables.Select(table => new BookingTable(table.Id, branchId, table.Capacity, table.IsActive)).ToArray() ?? [];
}

static async Task<IReadOnlyDictionary<(Guid BranchId, DayOfWeek DayOfWeek), BranchWorkingHours>> GetBranchWorkingHoursAsync(Guid branchId, IHttpClientFactory httpClientFactory, IConfiguration configuration, CancellationToken cancellationToken)
{
    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("booking-service");
    var hours = await client.GetFromJsonAsync<BranchWorkingHoursDto[]>($"{branchBaseUrl}/api/branches/{branchId}/working-hours", cancellationToken) ?? [];
    return hours.ToDictionary(
        item => (item.BranchId, (DayOfWeek)item.DayOfWeek),
        item => new BranchWorkingHours(item.BranchId, (DayOfWeek)item.DayOfWeek, item.OpensAt, item.ClosesAt, item.IsClosed));
}

static bool BranchIsOpen(Guid branchId, DateTimeOffset start, DateTimeOffset end, IReadOnlyDictionary<(Guid BranchId, DayOfWeek DayOfWeek), BranchWorkingHours> workingHours)
{
    if (!workingHours.TryGetValue((branchId, start.DayOfWeek), out var hours) || hours.IsClosed) return false;
    var startTime = TimeOnly.FromDateTime(start.DateTime);
    var endTime = TimeOnly.FromDateTime(end.DateTime);
    return hours.ClosesAt > hours.OpensAt ? startTime >= hours.OpensAt && endTime <= hours.ClosesAt : startTime >= hours.OpensAt || endTime <= hours.ClosesAt;
}

public sealed record BookingTable(Guid Id, Guid BranchId, int Capacity, bool IsActive);
public sealed record BranchWorkingHours(Guid BranchId, DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, bool IsClosed);
public sealed record FloorPlanDto(Guid BranchId, IReadOnlyCollection<HallDto> Halls, IReadOnlyCollection<ZoneDto> Zones, IReadOnlyCollection<TableDto> Tables);
public sealed record HallDto(Guid Id, Guid BranchId, string Name, string? Description);
public sealed record ZoneDto(Guid Id, Guid BranchId, string Name, string? Description, string? Color, bool IsActive);
public sealed record TableDto(Guid Id, Guid HallId, Guid? ZoneId, string Name, int Capacity, string Status, decimal XPosition, decimal YPosition, bool IsActive);
public sealed record BranchWorkingHoursDto(Guid BranchId, int DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, bool IsClosed);
public sealed record CreateBookingRequest(Guid BranchId, Guid TableId, Guid ClientId, DateTimeOffset StartTime, DateTimeOffset EndTime, int GuestsCount, Guid? HookahId, Guid? BowlId, Guid? MixId, string? Comment, decimal DepositAmount);
public sealed record BookingPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record CancelBookingRequest(string Reason);
public sealed record RescheduleBookingRequest(DateTimeOffset StartTime, DateTimeOffset EndTime, Guid TableId);
public sealed record BookingEligibility(Guid UserId, bool IsEligible, string? Reason);
