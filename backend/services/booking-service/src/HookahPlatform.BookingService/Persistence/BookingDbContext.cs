using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.BookingService.Persistence;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<BookingEntity>(entity =>
        {
            entity.ToTable("bookings");
            entity.HasKey(booking => booking.Id);
            entity.Property(booking => booking.TenantId).HasColumnName("tenant_id");
            entity.Property(booking => booking.ClientId).HasColumnName("client_id");
            entity.Property(booking => booking.BranchId).HasColumnName("branch_id");
            entity.Property(booking => booking.TableId).HasColumnName("table_id");
            entity.Property(booking => booking.HookahId).HasColumnName("hookah_id");
            entity.Property(booking => booking.BowlId).HasColumnName("bowl_id");
            entity.Property(booking => booking.MixId).HasColumnName("mix_id");
            entity.Property(booking => booking.StartTime).HasColumnName("start_time");
            entity.Property(booking => booking.EndTime).HasColumnName("end_time");
            entity.Property(booking => booking.GuestsCount).HasColumnName("guests_count");
            entity.Property(booking => booking.DepositAmount).HasColumnName("deposit_amount");
            entity.Property(booking => booking.PaymentId).HasColumnName("payment_id");
            entity.Property(booking => booking.DepositPaidAt).HasColumnName("deposit_paid_at");
            entity.Property(booking => booking.CreatedAt).HasColumnName("created_at");
        });
    }
}

public sealed class BookingEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public Guid ClientId { get; set; } public Guid BranchId { get; set; } public Guid TableId { get; set; } public Guid? HookahId { get; set; } public Guid? BowlId { get; set; } public Guid? MixId { get; set; } public DateTimeOffset StartTime { get; set; } public DateTimeOffset EndTime { get; set; } public int GuestsCount { get; set; } public string Status { get; set; } = string.Empty; public decimal DepositAmount { get; set; } public Guid? PaymentId { get; set; } public DateTimeOffset? DepositPaidAt { get; set; } public string? Comment { get; set; } public DateTimeOffset CreatedAt { get; set; } }
