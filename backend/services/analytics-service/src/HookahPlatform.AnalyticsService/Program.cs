using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("analytics-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var orders = new Dictionary<Guid, AnalyticsOrder>();
var bookings = new Dictionary<Guid, AnalyticsBooking>();
var tobaccoUsage = new Dictionary<(Guid BranchId, Guid TobaccoId), decimal>();
var mixStats = new Dictionary<Guid, MixMetric>();
var staffStats = new Dictionary<Guid, StaffMetric>();
var tableUsage = new Dictionary<(Guid BranchId, Guid TableId), TableMetric>();
SeedAnalytics(orders, bookings, tobaccoUsage, mixStats, staffStats, tableUsage);

app.MapPost("/api/analytics/events", (AnalyticsEventEnvelope envelope) =>
{
    switch (envelope.Event)
    {
        case nameof(OrderCreated):
            if (envelope.OrderId is null || envelope.BranchId is null || envelope.TableId is null || envelope.MixId is null)
            {
                return HttpResults.Validation("OrderCreated analytics event requires orderId, branchId, tableId and mixId.");
            }

            orders[envelope.OrderId.Value] = new AnalyticsOrder(envelope.OrderId.Value, envelope.BranchId.Value, envelope.TableId.Value, envelope.MixId.Value, envelope.HookahMasterId, envelope.TotalPrice ?? 0, OrderStatuses.New, envelope.OccurredAt);
            IncrementMix(mixStats, envelope.MixId.Value, 1, 0);
            IncrementTable(tableUsage, envelope.BranchId.Value, envelope.TableId.Value, TimeSpan.Zero);
            break;

        case nameof(OrderStatusChanged):
            if (envelope.OrderId is null || string.IsNullOrWhiteSpace(envelope.Status) || !orders.TryGetValue(envelope.OrderId.Value, out var order))
            {
                return HttpResults.Validation("OrderStatusChanged analytics event requires an existing order and status.");
            }

            orders[order.Id] = order with { Status = envelope.Status };
            break;

        case nameof(InventoryWrittenOff):
            if (envelope.BranchId is null || envelope.TobaccoId is null || envelope.AmountGrams is null)
            {
                return HttpResults.Validation("InventoryWrittenOff analytics event requires branchId, tobaccoId and amountGrams.");
            }

            tobaccoUsage[(envelope.BranchId.Value, envelope.TobaccoId.Value)] = tobaccoUsage.GetValueOrDefault((envelope.BranchId.Value, envelope.TobaccoId.Value)) + envelope.AmountGrams.Value;
            break;

        case nameof(BookingCreated):
            if (envelope.BookingId is null || envelope.BranchId is null || envelope.TableId is null)
            {
                return HttpResults.Validation("BookingCreated analytics event requires bookingId, branchId and tableId.");
            }

            bookings[envelope.BookingId.Value] = new AnalyticsBooking(envelope.BookingId.Value, envelope.BranchId.Value, envelope.TableId.Value, BookingStatuses.New, envelope.StartTime, envelope.EndTime, envelope.OccurredAt);
            break;

        case nameof(BookingConfirmed):
        case nameof(BookingCancelled):
            if (envelope.BookingId is null || !bookings.TryGetValue(envelope.BookingId.Value, out var booking))
            {
                return HttpResults.Validation($"{envelope.Event} analytics event requires an existing booking.");
            }

            bookings[booking.Id] = booking with
            {
                Status = envelope.Event == nameof(BookingConfirmed) ? BookingStatuses.Confirmed : BookingStatuses.Cancelled
            };
            break;

        case nameof(ReviewCreated):
            if (envelope.MixId is not null && envelope.Rating is not null)
            {
                IncrementMix(mixStats, envelope.MixId.Value, 0, envelope.Rating.Value);
            }
            break;
    }

    return Results.Accepted($"/api/analytics/events/{envelope.Event}", envelope);
});

app.MapGet("/api/analytics/dashboard", (Guid? branchId, DateOnly from, DateOnly to) =>
{
    var fromDate = from.ToDateTime(TimeOnly.MinValue);
    var toDate = to.ToDateTime(TimeOnly.MaxValue);
    var scopedOrders = orders.Values
        .Where(order => branchId is null || order.BranchId == branchId)
        .Where(order => order.CreatedAt.UtcDateTime >= fromDate && order.CreatedAt.UtcDateTime <= toDate)
        .ToArray();
    var scopedBookings = bookings.Values
        .Where(booking => branchId is null || booking.BranchId == branchId)
        .Where(booking => booking.CreatedAt.UtcDateTime >= fromDate && booking.CreatedAt.UtcDateTime <= toDate)
        .ToArray();

    var revenue = scopedOrders.Where(order => order.Status is OrderStatuses.Served or OrderStatuses.Smoking or OrderStatuses.Completed).Sum(order => order.TotalPrice);
    var averageCheck = scopedOrders.Length == 0 ? 0 : Math.Round(revenue / scopedOrders.Length, 2);
    var noShowCount = scopedBookings.Count(booking => booking.Status == BookingStatuses.NoShow);
    var noShowRate = scopedBookings.Length == 0 ? 0 : Math.Round(noShowCount * 100m / scopedBookings.Length, 2);

    return Results.Ok(new DashboardMetrics(revenue, scopedOrders.Length, averageCheck, scopedBookings.Length, noShowRate, from, to, branchId));
});

app.MapGet("/api/analytics/top-mixes", () => Results.Ok(mixStats.Values
    .OrderByDescending(metric => metric.OrdersCount)
    .ThenByDescending(metric => metric.AverageRating)
    .Take(20)
    .Select(metric => new TopMix(metric.MixId, metric.Name, metric.OrdersCount, metric.AverageRating))));

app.MapGet("/api/analytics/tobacco-usage", (Guid? branchId) =>
{
    var query = tobaccoUsage.AsEnumerable();
    if (branchId is not null)
    {
        query = query.Where(item => item.Key.BranchId == branchId);
    }

    return Results.Ok(query
        .OrderByDescending(item => item.Value)
        .Select(item => new TobaccoUsage(item.Key.BranchId, item.Key.TobaccoId, item.Value)));
});

app.MapGet("/api/analytics/staff-performance", () => Results.Ok(staffStats.Values
    .OrderByDescending(metric => metric.OrdersServed)
    .Select(metric => new StaffPerformance(metric.StaffId, metric.StaffName, metric.OrdersServed, metric.Rating, metric.AveragePrepareTime))));

app.MapGet("/api/analytics/table-load", (Guid? branchId) =>
{
    var query = tableUsage.Values.AsEnumerable();
    if (branchId is not null)
    {
        query = query.Where(metric => metric.BranchId == branchId);
    }

    return Results.Ok(query
        .OrderByDescending(metric => metric.LoadPercent)
        .Select(metric => new TableLoad(metric.BranchId, metric.TableId, metric.TableName, metric.LoadPercent)));
});

app.Run();

static void IncrementMix(IDictionary<Guid, MixMetric> mixStats, Guid mixId, int orderDelta, int rating)
{
    var metric = mixStats.TryGetValue(mixId, out var existing)
        ? existing
        : new MixMetric(mixId, $"Mix {mixId.ToString()[..8]}", 0, 0, 0);

    var ratingsCount = rating > 0 ? metric.RatingsCount + 1 : metric.RatingsCount;
    var ratingSum = rating > 0 ? metric.RatingSum + rating : metric.RatingSum;
    var averageRating = ratingsCount == 0 ? 0 : Math.Round(ratingSum / ratingsCount, 2);

    mixStats[mixId] = metric with
    {
        OrdersCount = metric.OrdersCount + orderDelta,
        RatingsCount = ratingsCount,
        RatingSum = ratingSum,
        AverageRating = averageRating
    };
}

static void IncrementTable(IDictionary<(Guid BranchId, Guid TableId), TableMetric> tableUsage, Guid branchId, Guid tableId, TimeSpan occupied)
{
    var key = (branchId, tableId);
    var metric = tableUsage.TryGetValue(key, out var existing)
        ? existing
        : new TableMetric(branchId, tableId, $"Table {tableId.ToString()[..8]}", 0);

    tableUsage[key] = metric with { LoadPercent = Math.Min(100, metric.LoadPercent + Math.Max(3, (decimal)occupied.TotalHours * 4)) };
}

static void SeedAnalytics(
    IDictionary<Guid, AnalyticsOrder> orders,
    IDictionary<Guid, AnalyticsBooking> bookings,
    IDictionary<(Guid BranchId, Guid TobaccoId), decimal> tobaccoUsage,
    IDictionary<Guid, MixMetric> mixStats,
    IDictionary<Guid, StaffMetric> staffStats,
    IDictionary<(Guid BranchId, Guid TableId), TableMetric> tableUsage)
{
    var branchId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var table1 = Guid.Parse("30000000-0000-0000-0000-000000000001");
    var table2 = Guid.Parse("30000000-0000-0000-0000-000000000002");
    var mixId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    var staffId = Guid.Parse("90000000-0000-0000-0000-000000000010");

    orders[Guid.NewGuid()] = new AnalyticsOrder(Guid.NewGuid(), branchId, table1, mixId, staffId, 850, OrderStatuses.Completed, DateTimeOffset.UtcNow.AddHours(-4));
    bookings[Guid.NewGuid()] = new AnalyticsBooking(Guid.NewGuid(), branchId, table2, BookingStatuses.Confirmed, DateTimeOffset.UtcNow.AddHours(2), DateTimeOffset.UtcNow.AddHours(4), DateTimeOffset.UtcNow.AddDays(-1));
    tobaccoUsage[(branchId, Guid.Parse("60000000-0000-0000-0000-000000000001"))] = 1250m;
    tobaccoUsage[(branchId, Guid.Parse("60000000-0000-0000-0000-000000000002"))] = 840m;
    mixStats[mixId] = new MixMetric(mixId, "Berry Ice", 86, 23, 110, 4.78m);
    staffStats[staffId] = new StaffMetric(staffId, "Hookah Master", 118, 4.9m, TimeSpan.FromMinutes(11));
    tableUsage[(branchId, table1)] = new TableMetric(branchId, table1, "Table 1", 78.4m);
    tableUsage[(branchId, table2)] = new TableMetric(branchId, table2, "Table 2", 64.2m);
}

public sealed record DashboardMetrics(decimal Revenue, int OrdersCount, decimal AverageCheck, int BookingsCount, decimal NoShowRate, DateOnly From, DateOnly To, Guid? BranchId);
public sealed record TopMix(Guid MixId, string Name, int OrdersCount, decimal Rating);
public sealed record TobaccoUsage(Guid BranchId, Guid TobaccoId, decimal Grams);
public sealed record StaffPerformance(Guid StaffId, string StaffName, int OrdersServed, decimal Rating, TimeSpan AveragePrepareTime);
public sealed record TableLoad(Guid BranchId, Guid TableId, string TableName, decimal LoadPercent);
public sealed record AnalyticsOrder(Guid Id, Guid BranchId, Guid TableId, Guid MixId, Guid? HookahMasterId, decimal TotalPrice, string Status, DateTimeOffset CreatedAt);
public sealed record AnalyticsBooking(Guid Id, Guid BranchId, Guid TableId, string Status, DateTimeOffset? StartTime, DateTimeOffset? EndTime, DateTimeOffset CreatedAt);
public sealed record MixMetric(Guid MixId, string Name, int OrdersCount, int RatingsCount, decimal RatingSum, decimal AverageRating = 0);
public sealed record StaffMetric(Guid StaffId, string StaffName, int OrdersServed, decimal Rating, TimeSpan AveragePrepareTime);
public sealed record TableMetric(Guid BranchId, Guid TableId, string TableName, decimal LoadPercent);
public sealed record AnalyticsEventEnvelope(
    string Event,
    DateTimeOffset OccurredAt,
    Guid? OrderId,
    Guid? BookingId,
    Guid? BranchId,
    Guid? TableId,
    Guid? MixId,
    Guid? TobaccoId,
    Guid? HookahMasterId,
    decimal? TotalPrice,
    decimal? AmountGrams,
    int? Rating,
    string? Status,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime);
