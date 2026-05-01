using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.AnalyticsService.Persistence;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<AnalyticsOrderEntity> Orders => Set<AnalyticsOrderEntity>();
    public DbSet<AnalyticsBookingEntity> Bookings => Set<AnalyticsBookingEntity>();
    public DbSet<AnalyticsTobaccoUsageEntity> TobaccoUsage => Set<AnalyticsTobaccoUsageEntity>();
    public DbSet<ProcessedIntegrationEventEntity> ProcessedEvents => Set<ProcessedIntegrationEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<AnalyticsOrderEntity>(entity => { entity.ToTable("analytics_orders"); entity.HasKey(order => order.Id); entity.Property(order => order.BranchId).HasColumnName("branch_id"); entity.Property(order => order.TableId).HasColumnName("table_id"); entity.Property(order => order.MixId).HasColumnName("mix_id"); entity.Property(order => order.HookahMasterId).HasColumnName("hookah_master_id"); entity.Property(order => order.TotalPrice).HasColumnName("total_price"); entity.Property(order => order.CreatedAt).HasColumnName("created_at"); });
        modelBuilder.Entity<AnalyticsBookingEntity>(entity => { entity.ToTable("analytics_bookings"); entity.HasKey(booking => booking.Id); entity.Property(booking => booking.BranchId).HasColumnName("branch_id"); entity.Property(booking => booking.TableId).HasColumnName("table_id"); entity.Property(booking => booking.StartTime).HasColumnName("start_time"); entity.Property(booking => booking.EndTime).HasColumnName("end_time"); entity.Property(booking => booking.CreatedAt).HasColumnName("created_at"); });
        modelBuilder.Entity<AnalyticsTobaccoUsageEntity>(entity => { entity.ToTable("analytics_tobacco_usage"); entity.HasKey(usage => new { usage.BranchId, usage.TobaccoId }); entity.Property(usage => usage.BranchId).HasColumnName("branch_id"); entity.Property(usage => usage.TobaccoId).HasColumnName("tobacco_id"); });
        modelBuilder.Entity<ProcessedIntegrationEventEntity>(entity => { entity.ToTable("processed_integration_events"); entity.HasKey(item => new { item.Handler, item.EventId }); entity.Property(item => item.EventId).HasColumnName("event_id"); entity.Property(item => item.ProcessedAt).HasColumnName("processed_at"); });
    }
}

public sealed class AnalyticsOrderEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public Guid TableId { get; set; } public Guid MixId { get; set; } public Guid? HookahMasterId { get; set; } public decimal TotalPrice { get; set; } public string Status { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } }
public sealed class AnalyticsBookingEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public Guid TableId { get; set; } public string Status { get; set; } = string.Empty; public DateTimeOffset? StartTime { get; set; } public DateTimeOffset? EndTime { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class AnalyticsTobaccoUsageEntity { public Guid BranchId { get; set; } public Guid TobaccoId { get; set; } public decimal Grams { get; set; } }
public sealed class ProcessedIntegrationEventEntity { public string Handler { get; set; } = string.Empty; public Guid EventId { get; set; } public DateTimeOffset ProcessedAt { get; set; } }
