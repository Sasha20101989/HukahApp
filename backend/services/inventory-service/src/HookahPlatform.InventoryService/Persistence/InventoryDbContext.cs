using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.InventoryService.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItemEntity> InventoryItems => Set<InventoryItemEntity>();
    public DbSet<InventoryMovementEntity> InventoryMovements => Set<InventoryMovementEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<InventoryItemEntity>(entity => { entity.ToTable("inventory_items"); entity.HasKey(item => item.Id); entity.HasIndex(item => new { item.BranchId, item.TobaccoId }).IsUnique(); entity.Property(item => item.BranchId).HasColumnName("branch_id"); entity.Property(item => item.TobaccoId).HasColumnName("tobacco_id"); entity.Property(item => item.StockGrams).HasColumnName("stock_grams"); entity.Property(item => item.MinStockGrams).HasColumnName("min_stock_grams"); entity.Property(item => item.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<InventoryMovementEntity>(entity => { entity.ToTable("inventory_movements"); entity.HasKey(movement => movement.Id); entity.Property(movement => movement.BranchId).HasColumnName("branch_id"); entity.Property(movement => movement.TobaccoId).HasColumnName("tobacco_id"); entity.Property(movement => movement.AmountGrams).HasColumnName("amount_grams"); entity.Property(movement => movement.OrderId).HasColumnName("order_id"); entity.Property(movement => movement.CreatedBy).HasColumnName("created_by"); entity.Property(movement => movement.CreatedAt).HasColumnName("created_at"); });
    }
}

public sealed class InventoryItemEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public Guid TobaccoId { get; set; } public decimal StockGrams { get; set; } public decimal MinStockGrams { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
public sealed class InventoryMovementEntity { public Guid Id { get; set; } public Guid BranchId { get; set; } public Guid TobaccoId { get; set; } public string Type { get; set; } = string.Empty; public decimal AmountGrams { get; set; } public string? Reason { get; set; } public Guid? OrderId { get; set; } public Guid? CreatedBy { get; set; } public DateTimeOffset CreatedAt { get; set; } }
