using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.TenantService.Persistence;

public sealed class TenantDbContext : DbContext
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options) { }

    public DbSet<TenantRecord> Tenants => Set<TenantRecord>();
    public DbSet<TenantSettingsRecord> TenantSettings => Set<TenantSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantRecord>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Slug).HasColumnName("slug");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<TenantSettingsRecord>(entity =>
        {
            entity.ToTable("tenant_settings");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.DefaultTimezone).HasColumnName("default_timezone");
            entity.Property(x => x.DefaultCurrency).HasColumnName("default_currency");
            entity.Property(x => x.RequireDeposit).HasColumnName("require_deposit");

            entity.HasOne<TenantRecord>()
                .WithOne()
                .HasForeignKey<TenantSettingsRecord>(x => x.TenantId);
        });
    }
}

public sealed class TenantRecord
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TenantSettingsRecord
{
    public Guid TenantId { get; set; }
    public string DefaultTimezone { get; set; } = "Europe/Moscow";
    public string DefaultCurrency { get; set; } = "RUB";
    public bool RequireDeposit { get; set; }
}

