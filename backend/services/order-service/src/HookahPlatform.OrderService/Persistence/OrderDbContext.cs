using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.OrderService.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItemEntity> OrderItems => Set<OrderItemEntity>();
    public DbSet<CoalChangeEntity> CoalChanges => Set<CoalChangeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<OrderEntity>(entity => { entity.ToTable("orders"); entity.HasKey(order => order.Id); entity.Property(order => order.BranchId).HasColumnName("branch_id"); entity.Property(order => order.TableId).HasColumnName("table_id"); entity.Property(order => order.ClientId).HasColumnName("client_id"); entity.Property(order => order.HookahMasterId).HasColumnName("hookah_master_id"); entity.Property(order => order.WaiterId).HasColumnName("waiter_id"); entity.Property(order => order.BookingId).HasColumnName("booking_id"); entity.Property(order => order.TotalPrice).HasColumnName("total_price"); entity.Property(order => order.CreatedAt).HasColumnName("created_at"); entity.Property(order => order.ServedAt).HasColumnName("served_at"); entity.Property(order => order.CompletedAt).HasColumnName("completed_at"); entity.Property(order => order.InventoryWrittenOffAt).HasColumnName("inventory_written_off_at"); entity.Property(order => order.PaymentId).HasColumnName("payment_id"); entity.Property(order => order.PaidAmount).HasColumnName("paid_amount"); entity.Property(order => order.PaidAt).HasColumnName("paid_at"); });
        modelBuilder.Entity<OrderItemEntity>(entity => { entity.ToTable("order_items"); entity.HasKey(item => item.Id); entity.Property(item => item.OrderId).HasColumnName("order_id"); entity.Property(item => item.HookahId).HasColumnName("hookah_id"); entity.Property(item => item.BowlId).HasColumnName("bowl_id"); entity.Property(item => item.MixId).HasColumnName("mix_id"); });
        modelBuilder.Entity<CoalChangeEntity>(entity => { entity.ToTable("coal_changes"); entity.HasKey(change => change.Id); entity.Property(change => change.OrderId).HasColumnName("order_id"); entity.Property(change => change.ChangedAt).HasColumnName("changed_at"); });
    }
}

public sealed class OrderEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public Guid TableId { get; set; } public Guid? ClientId { get; set; } public Guid? HookahMasterId { get; set; } public Guid? WaiterId { get; set; } public Guid? BookingId { get; set; } public string Status { get; set; } = string.Empty; public decimal TotalPrice { get; set; } public string? Comment { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? ServedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } public DateTimeOffset? InventoryWrittenOffAt { get; set; } public Guid? PaymentId { get; set; } public decimal PaidAmount { get; set; } public DateTimeOffset? PaidAt { get; set; } }
public sealed class OrderItemEntity { public Guid Id { get; set; } public Guid OrderId { get; set; } public Guid HookahId { get; set; } public Guid BowlId { get; set; } public Guid MixId { get; set; } public decimal Price { get; set; } public string Status { get; set; } = string.Empty; }
public sealed class CoalChangeEntity { public Guid Id { get; set; } public Guid OrderId { get; set; } public DateTimeOffset ChangedAt { get; set; } }
