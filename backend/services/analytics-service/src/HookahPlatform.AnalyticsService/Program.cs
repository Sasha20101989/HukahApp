using HookahPlatform.AnalyticsService.Persistence;
using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("analytics-service");
builder.AddPostgresDbContext<AnalyticsDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<AnalyticsDbContext>("analytics-service");

app.MapPost("/api/analytics/events", async (AnalyticsEventEnvelope envelope, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    switch (envelope.Event)
    {
        case nameof(OrderCreated):
            if (envelope.OrderId is null || envelope.BranchId is null || envelope.TableId is null || envelope.MixId is null) return HttpResults.Validation("OrderCreated analytics event requires orderId, branchId, tableId and mixId.");
            db.Orders.Add(new AnalyticsOrderEntity { Id = envelope.OrderId.Value, BranchId = envelope.BranchId.Value, TableId = envelope.TableId.Value, MixId = envelope.MixId.Value, HookahMasterId = envelope.HookahMasterId, TotalPrice = envelope.TotalPrice ?? 0, Status = OrderStatuses.New, CreatedAt = envelope.OccurredAt });
            break;
        case nameof(OrderStatusChanged):
            if (envelope.OrderId is null || string.IsNullOrWhiteSpace(envelope.Status)) return HttpResults.Validation("OrderStatusChanged analytics event requires orderId and status.");
            var order = await db.Orders.FirstOrDefaultAsync(candidate => candidate.Id == envelope.OrderId.Value, cancellationToken);
            if (order is not null) order.Status = envelope.Status;
            break;
        case nameof(InventoryWrittenOff):
            if (envelope.BranchId is null || envelope.TobaccoId is null || envelope.AmountGrams is null) return HttpResults.Validation("InventoryWrittenOff analytics event requires branchId, tobaccoId and amountGrams.");
            var usage = await db.TobaccoUsage.FirstOrDefaultAsync(candidate => candidate.BranchId == envelope.BranchId.Value && candidate.TobaccoId == envelope.TobaccoId.Value, cancellationToken);
            if (usage is null)
            {
                usage = new AnalyticsTobaccoUsageEntity { BranchId = envelope.BranchId.Value, TobaccoId = envelope.TobaccoId.Value, Grams = 0 };
                db.TobaccoUsage.Add(usage);
            }
            usage.Grams += envelope.AmountGrams.Value;
            break;
        case nameof(BookingCreated):
            if (envelope.BookingId is null || envelope.BranchId is null || envelope.TableId is null) return HttpResults.Validation("BookingCreated analytics event requires bookingId, branchId and tableId.");
            db.Bookings.Add(new AnalyticsBookingEntity { Id = envelope.BookingId.Value, BranchId = envelope.BranchId.Value, TableId = envelope.TableId.Value, Status = BookingStatuses.New, StartTime = envelope.StartTime, EndTime = envelope.EndTime, CreatedAt = envelope.OccurredAt });
            break;
        case nameof(BookingConfirmed):
        case nameof(BookingCancelled):
            if (envelope.BookingId is null) return HttpResults.Validation($"{envelope.Event} analytics event requires bookingId.");
            var booking = await db.Bookings.FirstOrDefaultAsync(candidate => candidate.Id == envelope.BookingId.Value, cancellationToken);
            if (booking is not null) booking.Status = envelope.Event == nameof(BookingConfirmed) ? BookingStatuses.Confirmed : BookingStatuses.Cancelled;
            break;
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Accepted($"/api/analytics/events/{envelope.Event}", envelope);
});

app.MapGet("/api/analytics/dashboard", async (Guid? branchId, DateOnly from, DateOnly to, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var fromDate = from.ToDateTime(TimeOnly.MinValue);
    var toDate = to.ToDateTime(TimeOnly.MaxValue);
    var scopedOrders = await db.Orders.AsNoTracking().Where(order => (branchId == null || order.BranchId == branchId) && order.CreatedAt.UtcDateTime >= fromDate && order.CreatedAt.UtcDateTime <= toDate).ToListAsync(cancellationToken);
    var scopedBookings = await db.Bookings.AsNoTracking().Where(booking => (branchId == null || booking.BranchId == branchId) && booking.CreatedAt.UtcDateTime >= fromDate && booking.CreatedAt.UtcDateTime <= toDate).ToListAsync(cancellationToken);
    var revenue = scopedOrders.Where(order => order.Status is OrderStatuses.Served or OrderStatuses.Smoking or OrderStatuses.Completed).Sum(order => order.TotalPrice);
    var averageCheck = scopedOrders.Count == 0 ? 0 : Math.Round(revenue / scopedOrders.Count, 2);
    var noShowCount = scopedBookings.Count(booking => booking.Status == BookingStatuses.NoShow);
    var noShowRate = scopedBookings.Count == 0 ? 0 : Math.Round(noShowCount * 100m / scopedBookings.Count, 2);
    return Results.Ok(new DashboardMetrics(revenue, scopedOrders.Count, averageCheck, scopedBookings.Count, noShowRate, from, to, branchId));
});

app.MapGet("/api/analytics/top-mixes", async (AnalyticsDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Orders.AsNoTracking().GroupBy(order => order.MixId).Select(group => new TopMix(group.Key, $"Mix {group.Key.ToString().Substring(0, 8)}", group.Count(), 0)).OrderByDescending(metric => metric.OrdersCount).Take(20).ToListAsync(cancellationToken)));

app.MapGet("/api/analytics/tobacco-usage", async (Guid? branchId, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.TobaccoUsage.AsNoTracking();
    if (branchId is not null) query = query.Where(item => item.BranchId == branchId);
    return Results.Ok(await query.OrderByDescending(item => item.Grams).Select(item => new TobaccoUsage(item.BranchId, item.TobaccoId, item.Grams)).ToListAsync(cancellationToken));
});

app.MapGet("/api/analytics/staff-performance", async (AnalyticsDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Orders.AsNoTracking().Where(order => order.HookahMasterId != null).GroupBy(order => order.HookahMasterId!.Value).Select(group => new StaffPerformance(group.Key, $"Staff {group.Key.ToString().Substring(0, 8)}", group.Count(), 0, TimeSpan.Zero)).OrderByDescending(metric => metric.OrdersServed).ToListAsync(cancellationToken)));

app.MapGet("/api/analytics/table-load", async (Guid? branchId, AnalyticsDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Bookings.AsNoTracking();
    if (branchId is not null) query = query.Where(booking => booking.BranchId == branchId);
    return Results.Ok(await query.GroupBy(booking => new { booking.BranchId, booking.TableId }).Select(group => new TableLoad(group.Key.BranchId, group.Key.TableId, $"Table {group.Key.TableId.ToString().Substring(0, 8)}", Math.Min(100, group.Count() * 5m))).OrderByDescending(metric => metric.LoadPercent).ToListAsync(cancellationToken));
});

app.Run();

public sealed record DashboardMetrics(decimal Revenue, int OrdersCount, decimal AverageCheck, int BookingsCount, decimal NoShowRate, DateOnly From, DateOnly To, Guid? BranchId);
public sealed record TopMix(Guid MixId, string Name, int OrdersCount, decimal Rating);
public sealed record TobaccoUsage(Guid BranchId, Guid TobaccoId, decimal Grams);
public sealed record StaffPerformance(Guid StaffId, string StaffName, int OrdersServed, decimal Rating, TimeSpan AveragePrepareTime);
public sealed record TableLoad(Guid BranchId, Guid TableId, string TableName, decimal LoadPercent);
public sealed record AnalyticsEventEnvelope(string Event, DateTimeOffset OccurredAt, Guid? OrderId, Guid? BookingId, Guid? BranchId, Guid? TableId, Guid? MixId, Guid? TobaccoId, Guid? HookahMasterId, decimal? TotalPrice, decimal? AmountGrams, int? Rating, string? Status, DateTimeOffset? StartTime, DateTimeOffset? EndTime);
