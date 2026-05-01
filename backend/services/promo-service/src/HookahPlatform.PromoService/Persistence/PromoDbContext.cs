using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.PromoService.Persistence;

public sealed class PromoDbContext(DbContextOptions<PromoDbContext> options) : DbContext(options)
{
    public DbSet<PromocodeEntity> Promocodes => Set<PromocodeEntity>();
    public DbSet<PromocodeRedemptionEntity> Redemptions => Set<PromocodeRedemptionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<PromocodeEntity>(entity => { entity.ToTable("promocodes"); entity.HasKey(promocode => promocode.Id); entity.HasIndex(promocode => promocode.Code).IsUnique(); entity.Property(promocode => promocode.DiscountType).HasColumnName("discount_type"); entity.Property(promocode => promocode.DiscountValue).HasColumnName("discount_value"); entity.Property(promocode => promocode.ValidFrom).HasColumnName("valid_from"); entity.Property(promocode => promocode.ValidTo).HasColumnName("valid_to"); entity.Property(promocode => promocode.MaxRedemptions).HasColumnName("max_redemptions"); entity.Property(promocode => promocode.PerClientLimit).HasColumnName("per_client_limit"); entity.Property(promocode => promocode.IsActive).HasColumnName("is_active"); });
        modelBuilder.Entity<PromocodeRedemptionEntity>(entity => { entity.ToTable("promocode_redemptions"); entity.HasKey(redemption => redemption.Id); entity.Property(redemption => redemption.ClientId).HasColumnName("client_id"); entity.Property(redemption => redemption.OrderId).HasColumnName("order_id"); entity.Property(redemption => redemption.OrderAmount).HasColumnName("order_amount"); entity.Property(redemption => redemption.DiscountAmount).HasColumnName("discount_amount"); entity.Property(redemption => redemption.CreatedAt).HasColumnName("created_at"); });
    }
}

public sealed class PromocodeEntity { public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string DiscountType { get; set; } = string.Empty; public decimal DiscountValue { get; set; } public DateOnly ValidFrom { get; set; } public DateOnly ValidTo { get; set; } public int? MaxRedemptions { get; set; } public int PerClientLimit { get; set; } public bool IsActive { get; set; } }
public sealed class PromocodeRedemptionEntity { public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public Guid ClientId { get; set; } public Guid? OrderId { get; set; } public decimal OrderAmount { get; set; } public decimal DiscountAmount { get; set; } public DateTimeOffset CreatedAt { get; set; } }
