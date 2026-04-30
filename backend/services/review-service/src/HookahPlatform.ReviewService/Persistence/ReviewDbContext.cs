using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.ReviewService.Persistence;

public sealed class ReviewDbContext(DbContextOptions<ReviewDbContext> options) : DbContext(options)
{
    public DbSet<ReviewEntity> Reviews => Set<ReviewEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewEntity>(entity => { entity.ToTable("reviews"); entity.HasKey(review => review.Id); entity.Property(review => review.ClientId).HasColumnName("client_id"); entity.Property(review => review.MixId).HasColumnName("mix_id"); entity.Property(review => review.OrderId).HasColumnName("order_id"); entity.Property(review => review.CreatedAt).HasColumnName("created_at"); });
    }
}

public sealed class ReviewEntity { public Guid Id { get; set; } public Guid ClientId { get; set; } public Guid? MixId { get; set; } public Guid? OrderId { get; set; } public int Rating { get; set; } public string? Text { get; set; } public DateTimeOffset CreatedAt { get; set; } }
