using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.BranchService.Persistence;

public sealed class BranchDbContext(DbContextOptions<BranchDbContext> options) : DbContext(options)
{
    public DbSet<BranchEntity> Branches => Set<BranchEntity>();
    public DbSet<HallEntity> Halls => Set<HallEntity>();
    public DbSet<ZoneEntity> Zones => Set<ZoneEntity>();
    public DbSet<BranchWorkingHoursEntity> WorkingHours => Set<BranchWorkingHoursEntity>();
    public DbSet<TableEntity> Tables => Set<TableEntity>();
    public DbSet<HookahEntity> Hookahs => Set<HookahEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<BranchEntity>(entity => { entity.ToTable("branches"); entity.HasKey(branch => branch.Id); entity.Property(branch => branch.IsActive).HasColumnName("is_active"); entity.Property(branch => branch.CreatedAt).HasColumnName("created_at"); });
        modelBuilder.Entity<HallEntity>(entity => { entity.ToTable("halls"); entity.HasKey(hall => hall.Id); entity.Property(hall => hall.BranchId).HasColumnName("branch_id"); });
        modelBuilder.Entity<ZoneEntity>(entity => { entity.ToTable("zones"); entity.HasKey(zone => zone.Id); entity.Property(zone => zone.BranchId).HasColumnName("branch_id"); entity.Property(zone => zone.XPosition).HasColumnName("x_position"); entity.Property(zone => zone.YPosition).HasColumnName("y_position"); entity.Property(zone => zone.Width).HasColumnName("width"); entity.Property(zone => zone.Height).HasColumnName("height"); entity.Property(zone => zone.IsActive).HasColumnName("is_active"); });
        modelBuilder.Entity<BranchWorkingHoursEntity>(entity => { entity.ToTable("branch_working_hours"); entity.HasKey(hours => new { hours.BranchId, hours.DayOfWeek }); entity.Property(hours => hours.BranchId).HasColumnName("branch_id"); entity.Property(hours => hours.DayOfWeek).HasColumnName("day_of_week"); entity.Property(hours => hours.OpensAt).HasColumnName("opens_at"); entity.Property(hours => hours.ClosesAt).HasColumnName("closes_at"); entity.Property(hours => hours.IsClosed).HasColumnName("is_closed"); });
        modelBuilder.Entity<TableEntity>(entity => { entity.ToTable("tables"); entity.HasKey(table => table.Id); entity.Property(table => table.HallId).HasColumnName("hall_id"); entity.Property(table => table.ZoneId).HasColumnName("zone_id"); entity.Property(table => table.XPosition).HasColumnName("x_position"); entity.Property(table => table.YPosition).HasColumnName("y_position"); entity.Property(table => table.IsActive).HasColumnName("is_active"); });
        modelBuilder.Entity<HookahEntity>(entity => { entity.ToTable("hookahs"); entity.HasKey(hookah => hookah.Id); entity.Property(hookah => hookah.BranchId).HasColumnName("branch_id"); entity.Property(hookah => hookah.PhotoUrl).HasColumnName("photo_url"); entity.Property(hookah => hookah.LastServiceAt).HasColumnName("last_service_at"); });
    }
}

public sealed class BranchEntity { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string Address { get; set; } = string.Empty; public string Phone { get; set; } = string.Empty; public string Timezone { get; set; } = string.Empty; public bool IsActive { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class HallEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public string Name { get; set; } = string.Empty; public string? Description { get; set; } }
public sealed class ZoneEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public string Name { get; set; } = string.Empty; public string? Description { get; set; } public string? Color { get; set; } public decimal XPosition { get; set; } public decimal YPosition { get; set; } public decimal Width { get; set; } public decimal Height { get; set; } public bool IsActive { get; set; } }
public sealed class BranchWorkingHoursEntity { public Guid BranchId { get; set; } public int DayOfWeek { get; set; } public TimeOnly OpensAt { get; set; } public TimeOnly ClosesAt { get; set; } public bool IsClosed { get; set; } }
public sealed class TableEntity { public Guid Id { get; set; } public Guid HallId { get; set; } public Guid? ZoneId { get; set; } public string Name { get; set; } = string.Empty; public int Capacity { get; set; } public string Status { get; set; } = string.Empty; public decimal XPosition { get; set; } public decimal YPosition { get; set; } public bool IsActive { get; set; } }
public sealed class HookahEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public string Name { get; set; } = string.Empty; public string Brand { get; set; } = string.Empty; public string Model { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public string? PhotoUrl { get; set; } public DateTimeOffset? LastServiceAt { get; set; } }
