using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.MixologyService.Persistence;

public sealed class MixologyDbContext(DbContextOptions<MixologyDbContext> options) : DbContext(options)
{
    public DbSet<BowlEntity> Bowls => Set<BowlEntity>();
    public DbSet<TobaccoEntity> Tobaccos => Set<TobaccoEntity>();
    public DbSet<MixEntity> Mixes => Set<MixEntity>();
    public DbSet<MixItemEntity> MixItems => Set<MixItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<BowlEntity>(entity => { entity.ToTable("bowls"); entity.HasKey(bowl => bowl.Id); entity.Property(bowl => bowl.CapacityGrams).HasColumnName("capacity_grams"); entity.Property(bowl => bowl.RecommendedStrength).HasColumnName("recommended_strength"); entity.Property(bowl => bowl.AverageSmokeMinutes).HasColumnName("average_smoke_minutes"); entity.Property(bowl => bowl.IsActive).HasColumnName("is_active"); });
        modelBuilder.Entity<TobaccoEntity>(entity => { entity.ToTable("tobaccos"); entity.HasKey(tobacco => tobacco.Id); entity.Property(tobacco => tobacco.CostPerGram).HasColumnName("cost_per_gram"); entity.Property(tobacco => tobacco.IsActive).HasColumnName("is_active"); entity.Property(tobacco => tobacco.PhotoUrl).HasColumnName("photo_url"); });
        modelBuilder.Entity<MixEntity>(entity =>
        {
            entity.ToTable("mixes");
            entity.HasKey(mix => mix.Id);
            entity.Property(mix => mix.TenantId).HasColumnName("tenant_id");
            entity.Property(mix => mix.BowlId).HasColumnName("bowl_id");
            entity.Property(mix => mix.TasteProfile).HasColumnName("taste_profile");
            entity.Property(mix => mix.TotalGrams).HasColumnName("total_grams");
            entity.Property(mix => mix.IsPublic).HasColumnName("is_public");
            entity.Property(mix => mix.IsActive).HasColumnName("is_active");
            entity.Property(mix => mix.CreatedBy).HasColumnName("created_by");
            entity.Property(mix => mix.CreatedAt).HasColumnName("created_at");
        });
        modelBuilder.Entity<MixItemEntity>(entity =>
        {
            entity.ToTable("mix_items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TenantId).HasColumnName("tenant_id");
            entity.Property(item => item.MixId).HasColumnName("mix_id");
            entity.Property(item => item.TobaccoId).HasColumnName("tobacco_id");
        });
    }
}

public sealed class BowlEntity { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string Type { get; set; } = string.Empty; public decimal CapacityGrams { get; set; } public string RecommendedStrength { get; set; } = string.Empty; public int AverageSmokeMinutes { get; set; } public bool IsActive { get; set; } }
public sealed class TobaccoEntity { public Guid Id { get; set; } public string Brand { get; set; } = string.Empty; public string Line { get; set; } = string.Empty; public string Flavor { get; set; } = string.Empty; public string Strength { get; set; } = string.Empty; public string Category { get; set; } = string.Empty; public string? Description { get; set; } public decimal CostPerGram { get; set; } public bool IsActive { get; set; } public string? PhotoUrl { get; set; } }
public sealed class MixEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public string Name { get; set; } = string.Empty; public string? Description { get; set; } public Guid BowlId { get; set; } public string Strength { get; set; } = string.Empty; public string TasteProfile { get; set; } = string.Empty; public decimal TotalGrams { get; set; } public decimal Price { get; set; } public decimal Cost { get; set; } public decimal Margin { get; set; } public bool IsPublic { get; set; } public bool IsActive { get; set; } public Guid? CreatedBy { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class MixItemEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public Guid MixId { get; set; } public Guid TobaccoId { get; set; } public decimal Percent { get; set; } public decimal Grams { get; set; } }
