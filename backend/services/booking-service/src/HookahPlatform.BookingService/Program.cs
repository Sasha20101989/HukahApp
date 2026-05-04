using HookahPlatform.BookingService.Persistence;
using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using Microsoft.Extensions.Caching.Distributed;
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

app.MapGet("/api/bookings/availability", async (Guid branchId, DateOnly date, TimeOnly time, int guestsCount, BookingDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var availabilityCacheKey = AvailabilityCacheKey(branchId, date, time, guestsCount);
    var cachedAvailability = await cache.GetJsonAsync<BookingTable[]>(availabilityCacheKey, cancellationToken);
    if (cachedAvailability is not null) return Results.Ok(cachedAvailability);

    var start = new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero);
    var end = start.AddHours(2);
    var workingHours = await GetBranchWorkingHoursAsync(branchId, httpClientFactory, configuration, cache, cancellationToken);
    if (!BranchIsOpen(branchId, start, end, workingHours)) return Results.Ok(Array.Empty<BookingTable>());

    var tables = await GetBranchTablesAsync(branchId, httpClientFactory, configuration, cache, cancellationToken);

    var branchBookings = await db.Bookings.AsNoTracking().Where(booking => booking.BranchId == branchId && booking.Status != BookingStatuses.Cancelled && booking.Status != BookingStatuses.NoShow).ToListAsync(cancellationToken);
    var branchHolds = await GetActiveHoldsAsync(cache, branchId, date, cancellationToken);
    var available = tables
        .Where(table => table.BranchId == branchId && table.IsActive && table.Capacity >= guestsCount)
        .Where(table => branchBookings.All(booking => booking.TableId != table.Id || !DomainRules.Intersects(start, end, booking.StartTime, booking.EndTime)))
        .Where(table => branchHolds.All(hold => hold.TableId != table.Id || !DomainRules.Intersects(start, end, hold.StartTime, hold.EndTime)))
        .OrderBy(table => table.Capacity);

    var availableTables = available.ToArray();
    await cache.SetJsonAsync(availabilityCacheKey, availableTables, TimeSpan.FromSeconds(10), cancellationToken);
    return Results.Ok(availableTables);
});

app.MapPost("/api/bookings/holds", async (CreateBookingHoldRequest request, HttpContext context, BookingDbContext db, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (!CanActForClient(context, request.ClientId))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Client booking holds can only be created for the current user."), statusCode: StatusCodes.Status403Forbidden);
    }

    var table = (await GetBranchTablesAsync(request.BranchId, httpClientFactory, configuration, cache, cancellationToken)).FirstOrDefault(candidate => candidate.Id == request.TableId);
    if (table is null) return HttpResults.NotFound("Table", request.TableId);
    if (table.Capacity < request.GuestsCount) return HttpResults.Validation("Table capacity is less than guests count.");

    var date = DateOnly.FromDateTime(request.StartTime.UtcDateTime);
    var bookingIntersects = await db.Bookings.AnyAsync(booking =>
        booking.TableId == request.TableId &&
        booking.Status != BookingStatuses.Cancelled &&
        booking.Status != BookingStatuses.NoShow &&
        request.StartTime < booking.EndTime &&
        request.EndTime > booking.StartTime,
        cancellationToken);
    if (bookingIntersects) return HttpResults.Conflict("Booking time intersects with an existing booking.");

    var holdIntersects = (await GetActiveHoldsAsync(cache, request.BranchId, date, cancellationToken)).Any(hold =>
        hold.TableId == request.TableId &&
        request.StartTime < hold.EndTime &&
        request.EndTime > hold.StartTime);
    if (holdIntersects) return HttpResults.Conflict("Booking time is temporarily held by another client.");

    var now = DateTimeOffset.UtcNow;
    var hold = new BookingHold(Guid.NewGuid(), request.BranchId, request.TableId, request.ClientId, request.StartTime, request.EndTime, request.GuestsCount, now, now.AddMinutes(10));
    await StoreHoldAsync(cache, hold, cancellationToken);
    await cache.RemoveAsync(AvailabilityCacheKey(request.BranchId, date, TimeOnly.FromDateTime(request.StartTime.UtcDateTime), request.GuestsCount), cancellationToken);
    return Results.Created($"/api/bookings/holds/{hold.Id}", hold);
});

app.MapDelete("/api/bookings/holds/{id:guid}", async (Guid id, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var hold = await cache.GetJsonAsync<BookingHold>(HoldKey(id), cancellationToken);
    await cache.RemoveAsync(HoldKey(id), cancellationToken);
    if (hold is not null) await cache.RemoveAsync(AvailabilityCacheKey(hold.BranchId, DateOnly.FromDateTime(hold.StartTime.UtcDateTime), TimeOnly.FromDateTime(hold.StartTime.UtcDateTime), hold.GuestsCount), cancellationToken);
    return Results.NoContent();
});

app.MapPost("/api/bookings", async (CreateBookingRequest request, HttpContext context, BookingDbContext db, IEventPublisher events, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    if (!CanActForClient(context, request.ClientId))
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Client bookings can only be created for the current user."), statusCode: StatusCodes.Status403Forbidden);
    }

    var table = (await GetBranchTablesAsync(request.BranchId, httpClientFactory, configuration, cache, cancellationToken)).FirstOrDefault(candidate => candidate.Id == request.TableId);
    if (table is null) return HttpResults.NotFound("Table", request.TableId);
    if (table.Capacity < request.GuestsCount) return HttpResults.Validation("Table capacity is less than guests count.");
    var workingHours = await GetBranchWorkingHoursAsync(request.BranchId, httpClientFactory, configuration, cache, cancellationToken);
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

    var date = DateOnly.FromDateTime(request.StartTime.UtcDateTime);
    var activeHolds = await GetActiveHoldsAsync(cache, request.BranchId, date, cancellationToken);
    if (request.HoldId is not null)
    {
        var hold = activeHolds.FirstOrDefault(candidate => candidate.Id == request.HoldId.Value);
        if (hold is null) return HttpResults.Conflict("Booking hold has expired or does not exist.");
        if (hold.ClientId != request.ClientId || hold.TableId != request.TableId || hold.StartTime != request.StartTime || hold.EndTime != request.EndTime) return HttpResults.Conflict("Booking hold does not match the booking request.");
    }
    else if (activeHolds.Any(hold => hold.TableId == request.TableId && request.StartTime < hold.EndTime && request.EndTime > hold.StartTime))
    {
        return HttpResults.Conflict("Booking time is temporarily held by another client.");
    }

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
    if (request.HoldId is not null) await cache.RemoveAsync(HoldKey(request.HoldId.Value), cancellationToken);
    await cache.RemoveAsync(AvailabilityCacheKey(request.BranchId, DateOnly.FromDateTime(request.StartTime.UtcDateTime), TimeOnly.FromDateTime(request.StartTime.UtcDateTime), request.GuestsCount), cancellationToken);
    return Results.Created($"/api/bookings/{booking.Id}", booking);
});

app.MapGet("/api/bookings", async (Guid? branchId, DateOnly? date, string? status, HttpContext context, BookingDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Bookings.AsNoTracking();
    if (branchId is not null) query = query.Where(booking => booking.BranchId == branchId);
    if (!IsServiceRequest(context) && !HasForwardedPermission(context, PermissionCodes.BookingsManage))
    {
        var currentUserId = GetForwardedUserId(context);
        if (currentUserId is null)
        {
            return Results.Json(new ProblemDetailsDto("user_context_required", "Forwarded user context is required."), statusCode: StatusCodes.Status401Unauthorized);
        }

        query = query.Where(booking => booking.ClientId == currentUserId.Value);
    }
    if (date is not null) query = query.Where(booking => DateOnly.FromDateTime(booking.StartTime.UtcDateTime) == date);
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!allowedBookingStatuses.Contains(status)) return HttpResults.Validation($"Unsupported booking status '{status}'.");
        query = query.Where(booking => booking.Status == status);
    }
    return Results.Ok(await query.OrderBy(booking => booking.StartTime).ToListAsync(cancellationToken));
});

app.MapGet("/api/bookings/{id:guid}", async (Guid id, HttpContext context, BookingDbContext db, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);

    if (!IsServiceRequest(context) && !HasForwardedPermission(context, PermissionCodes.BookingsManage) && GetForwardedUserId(context) != booking.ClientId)
    {
        return Results.Json(new ProblemDetailsDto("forbidden", "Booking can only be read by the client who owns it or booking managers."), statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/confirm", async (Guid id, BookingDbContext db, IEventPublisher events, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    if (booking.DepositAmount > 0 && booking.DepositPaidAt is null) return HttpResults.Conflict("Booking with required deposit cannot be confirmed before payment.");
    booking.Status = BookingStatuses.Confirmed;
    var confirmed = new BookingConfirmed(id, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(confirmed);
    await db.SaveChangesAsync(cancellationToken);
    await InvalidateAvailabilityForBookingAsync(cache, booking, cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, confirmed, outboxMessage, cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/payment-succeeded", async (Guid id, BookingPaymentSucceededRequest request, BookingDbContext db, IEventPublisher events, IDistributedCache cache, CancellationToken cancellationToken) =>
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
    await InvalidateAvailabilityForBookingAsync(cache, booking, cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, outboxEvents, outboxMessages, cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/cancel", async (Guid id, CancelBookingRequest request, BookingDbContext db, IEventPublisher events, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    var reason = request.Reason?.Trim();
    if (string.IsNullOrWhiteSpace(reason)) return HttpResults.Validation("Cancellation reason is required.");
    booking.Status = BookingStatuses.Cancelled;
    var cancelled = new BookingCancelled(id, reason, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(cancelled);
    await db.SaveChangesAsync(cancellationToken);
    await InvalidateAvailabilityForBookingAsync(cache, booking, cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, cancelled, outboxMessage, cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/reschedule", async (Guid id, RescheduleBookingRequest request, BookingDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    var previous = new BookingAvailabilityFingerprint(booking.BranchId, booking.StartTime, booking.GuestsCount);
    var intersects = await db.Bookings.AnyAsync(existing => existing.Id != id && existing.TableId == request.TableId && existing.Status != BookingStatuses.Cancelled && existing.Status != BookingStatuses.NoShow && request.StartTime < existing.EndTime && request.EndTime > existing.StartTime, cancellationToken);
    if (intersects) return HttpResults.Conflict("Reschedule time intersects with an existing booking.");
    booking.StartTime = request.StartTime;
    booking.EndTime = request.EndTime;
    booking.TableId = request.TableId;
    await db.SaveChangesAsync(cancellationToken);
    await InvalidateAvailabilityAsync(cache, previous, cancellationToken);
    await InvalidateAvailabilityForBookingAsync(cache, booking, cancellationToken);
    return Results.Ok(booking);
});

app.MapPatch("/api/bookings/{id:guid}/no-show", async (Guid id, BookingDbContext db, IDistributedCache cache, CancellationToken cancellationToken) => await SetBookingStatusAsync(id, BookingStatuses.NoShow, db, cache, cancellationToken));
app.MapPatch("/api/bookings/{id:guid}/client-arrived", async (Guid id, BookingDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    if (booking.Status != BookingStatuses.Confirmed) return HttpResults.Conflict("Only confirmed bookings can be marked as client arrived.");
    booking.Status = BookingStatuses.ClientArrived;
    await db.SaveChangesAsync(cancellationToken);
    await InvalidateAvailabilityForBookingAsync(cache, booking, cancellationToken);
    return Results.Ok(booking);
});
app.MapPatch("/api/bookings/{id:guid}/complete", async (Guid id, BookingDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    if (booking.Status is not BookingStatuses.ClientArrived and not BookingStatuses.Confirmed) return HttpResults.Conflict("Only confirmed or arrived bookings can be completed.");
    booking.Status = BookingStatuses.Completed;
    await db.SaveChangesAsync(cancellationToken);
    await InvalidateAvailabilityForBookingAsync(cache, booking, cancellationToken);
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

static async Task<IResult> SetBookingStatusAsync(Guid id, string status, BookingDbContext db, IDistributedCache cache, CancellationToken cancellationToken)
{
    var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    if (booking is null) return HttpResults.NotFound("Booking", id);
    booking.Status = status;
    await db.SaveChangesAsync(cancellationToken);
    await InvalidateAvailabilityForBookingAsync(cache, booking, cancellationToken);
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

static async Task<IReadOnlyCollection<BookingTable>> GetBranchTablesAsync(Guid branchId, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken)
{
    var cacheKey = $"booking:branch:{branchId}:tables";
    var cached = await cache.GetJsonAsync<BookingTable[]>(cacheKey, cancellationToken);
    if (cached is not null) return cached;

    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("booking-service");
    var floorPlan = await client.GetFromJsonAsync<FloorPlanDto>($"{branchBaseUrl}/api/branches/{branchId}/floor-plan", cancellationToken);
    var tables = floorPlan?.Tables.Select(table => new BookingTable(table.Id, branchId, table.Capacity, table.IsActive)).ToArray() ?? [];
    await cache.SetJsonAsync(cacheKey, tables, TimeSpan.FromSeconds(45), cancellationToken);
    return tables;
}

static async Task<IReadOnlyDictionary<(Guid BranchId, DayOfWeek DayOfWeek), BranchWorkingHours>> GetBranchWorkingHoursAsync(Guid branchId, IHttpClientFactory httpClientFactory, IConfiguration configuration, IDistributedCache cache, CancellationToken cancellationToken)
{
    var cacheKey = $"booking:branch:{branchId}:working-hours";
    var cached = await cache.GetJsonAsync<BranchWorkingHours[]>(cacheKey, cancellationToken);
    if (cached is not null) return ToWorkingHoursDictionary(cached);

    var branchBaseUrl = configuration["Services:branch-service:BaseUrl"] ?? "http://branch-service:8080";
    var client = httpClientFactory.CreateClient("booking-service");
    var hours = await client.GetFromJsonAsync<BranchWorkingHoursDto[]>($"{branchBaseUrl}/api/branches/{branchId}/working-hours", cancellationToken) ?? [];
    var mapped = hours.Select(item => new BranchWorkingHours(item.BranchId, (DayOfWeek)item.DayOfWeek, item.OpensAt, item.ClosesAt, item.IsClosed)).ToArray();
    await cache.SetJsonAsync(cacheKey, mapped, TimeSpan.FromMinutes(5), cancellationToken);
    return ToWorkingHoursDictionary(mapped);
}

static IReadOnlyDictionary<(Guid BranchId, DayOfWeek DayOfWeek), BranchWorkingHours> ToWorkingHoursDictionary(IReadOnlyCollection<BranchWorkingHours> hours)
{
    return hours.ToDictionary(
        item => (item.BranchId, (DayOfWeek)item.DayOfWeek),
        item => item);
}

static string AvailabilityCacheKey(Guid branchId, DateOnly date, TimeOnly time, int guestsCount) => $"booking:availability:{branchId}:{date:yyyyMMdd}:{time:HHmm}:{guestsCount}";
static string HoldKey(Guid holdId) => $"booking:hold:{holdId}";
static string HoldIndexKey(Guid branchId, DateOnly date) => $"booking:holds:{branchId}:{date:yyyyMMdd}";

static async Task StoreHoldAsync(IDistributedCache cache, BookingHold hold, CancellationToken cancellationToken)
{
    var ttl = hold.ExpiresAt - DateTimeOffset.UtcNow;
    if (ttl <= TimeSpan.Zero) return;

    await cache.SetJsonAsync(HoldKey(hold.Id), hold, ttl, cancellationToken);
    var date = DateOnly.FromDateTime(hold.StartTime.UtcDateTime);
    var indexKey = HoldIndexKey(hold.BranchId, date);
    var ids = await cache.GetJsonAsync<Guid[]>(indexKey, cancellationToken) ?? [];
    var nextIds = ids.Append(hold.Id).Distinct().ToArray();
    await cache.SetJsonAsync(indexKey, nextIds, TimeSpan.FromDays(1), cancellationToken);
}

static async Task<IReadOnlyCollection<BookingHold>> GetActiveHoldsAsync(IDistributedCache cache, Guid branchId, DateOnly date, CancellationToken cancellationToken)
{
    var ids = await cache.GetJsonAsync<Guid[]>(HoldIndexKey(branchId, date), cancellationToken) ?? [];
    var now = DateTimeOffset.UtcNow;
    var holds = new List<BookingHold>();
    foreach (var id in ids)
    {
        var hold = await cache.GetJsonAsync<BookingHold>(HoldKey(id), cancellationToken);
        if (hold is null || hold.ExpiresAt <= now) continue;
        holds.Add(hold);
    }
    return holds;
}

static Task InvalidateAvailabilityForBookingAsync(IDistributedCache cache, BookingEntity booking, CancellationToken cancellationToken)
{
    return InvalidateAvailabilityAsync(cache, new BookingAvailabilityFingerprint(booking.BranchId, booking.StartTime, booking.GuestsCount), cancellationToken);
}

static Task InvalidateAvailabilityAsync(IDistributedCache cache, BookingAvailabilityFingerprint fingerprint, CancellationToken cancellationToken)
{
    return cache.RemoveAsync(
        AvailabilityCacheKey(fingerprint.BranchId, DateOnly.FromDateTime(fingerprint.StartTime.UtcDateTime), TimeOnly.FromDateTime(fingerprint.StartTime.UtcDateTime), fingerprint.GuestsCount),
        cancellationToken);
}

static bool BranchIsOpen(Guid branchId, DateTimeOffset start, DateTimeOffset end, IReadOnlyDictionary<(Guid BranchId, DayOfWeek DayOfWeek), BranchWorkingHours> workingHours)
{
    if (!workingHours.TryGetValue((branchId, start.DayOfWeek), out var hours) || hours.IsClosed) return false;
    var startTime = TimeOnly.FromDateTime(start.DateTime);
    var endTime = TimeOnly.FromDateTime(end.DateTime);
    return hours.ClosesAt > hours.OpensAt ? startTime >= hours.OpensAt && endTime <= hours.ClosesAt : startTime >= hours.OpensAt || endTime <= hours.ClosesAt;
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

static bool CanActForClient(HttpContext context, Guid clientId)
{
    return IsServiceRequest(context) ||
           HasForwardedPermission(context, PermissionCodes.BookingsManage) ||
           GetForwardedUserId(context) == clientId;
}

public sealed record BookingTable(Guid Id, Guid BranchId, int Capacity, bool IsActive);
public sealed record BranchWorkingHours(Guid BranchId, DayOfWeek DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, bool IsClosed);
public sealed record FloorPlanDto(Guid BranchId, IReadOnlyCollection<HallDto> Halls, IReadOnlyCollection<ZoneDto> Zones, IReadOnlyCollection<TableDto> Tables);
public sealed record HallDto(Guid Id, Guid BranchId, string Name, string? Description);
public sealed record ZoneDto(Guid Id, Guid BranchId, string Name, string? Description, string? Color, bool IsActive);
public sealed record TableDto(Guid Id, Guid HallId, Guid? ZoneId, string Name, int Capacity, string Status, decimal XPosition, decimal YPosition, bool IsActive);
public sealed record BranchWorkingHoursDto(Guid BranchId, int DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt, bool IsClosed);
public sealed record CreateBookingRequest(Guid BranchId, Guid TableId, Guid ClientId, DateTimeOffset StartTime, DateTimeOffset EndTime, int GuestsCount, Guid? HookahId, Guid? BowlId, Guid? MixId, string? Comment, decimal DepositAmount, Guid? HoldId = null);
public sealed record CreateBookingHoldRequest(Guid BranchId, Guid TableId, Guid ClientId, DateTimeOffset StartTime, DateTimeOffset EndTime, int GuestsCount);
public sealed record BookingHold(Guid Id, Guid BranchId, Guid TableId, Guid ClientId, DateTimeOffset StartTime, DateTimeOffset EndTime, int GuestsCount, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record BookingAvailabilityFingerprint(Guid BranchId, DateTimeOffset StartTime, int GuestsCount);
public sealed record BookingPaymentSucceededRequest(Guid PaymentId, decimal Amount);
public sealed record CancelBookingRequest(string Reason);
public sealed record RescheduleBookingRequest(DateTimeOffset StartTime, DateTimeOffset EndTime, Guid TableId);
public sealed record BookingEligibility(Guid UserId, bool IsEligible, string? Reason);
