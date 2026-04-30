using HookahPlatform.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("analytics-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

app.MapGet("/api/analytics/dashboard", (Guid? branchId, DateOnly from, DateOnly to) => Results.Ok(new DashboardMetrics(
    Revenue: 450000,
    OrdersCount: 320,
    AverageCheck: 1406.25m,
    BookingsCount: 120,
    NoShowRate: 8.5m,
    From: from,
    To: to,
    BranchId: branchId)));

app.MapGet("/api/analytics/top-mixes", () => Results.Ok(new[]
{
    new TopMix("Berry Ice", 86, 4.8m),
    new TopMix("Sweet Fresh", 64, 4.7m),
    new TopMix("Dark Citrus", 42, 4.6m)
}));

app.MapGet("/api/analytics/tobacco-usage", () => Results.Ok(new[]
{
    new TobaccoUsage("Darkside Strawberry", 1250m),
    new TobaccoUsage("Musthave Mint", 840m),
    new TobaccoUsage("Element Blueberry", 760m)
}));

app.MapGet("/api/analytics/staff-performance", () => Results.Ok(new[]
{
    new StaffPerformance("Hookah Master", 118, 4.9m, TimeSpan.FromMinutes(11))
}));

app.MapGet("/api/analytics/table-load", () => Results.Ok(new[]
{
    new TableLoad("Table 1", 78.4m),
    new TableLoad("Table 2", 64.2m)
}));

app.Run();

public sealed record DashboardMetrics(decimal Revenue, int OrdersCount, decimal AverageCheck, int BookingsCount, decimal NoShowRate, DateOnly From, DateOnly To, Guid? BranchId);
public sealed record TopMix(string Name, int OrdersCount, decimal Rating);
public sealed record TobaccoUsage(string Tobacco, decimal Grams);
public sealed record StaffPerformance(string StaffName, int OrdersServed, decimal Rating, TimeSpan AveragePrepareTime);
public sealed record TableLoad(string TableName, decimal LoadPercent);
