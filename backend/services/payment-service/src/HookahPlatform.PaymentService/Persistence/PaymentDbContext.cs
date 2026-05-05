using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.PaymentService.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<TenantPaymentProviderEntity> TenantPaymentProviders => Set<TenantPaymentProviderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<PaymentEntity>(entity => { entity.ToTable("payments"); entity.HasKey(payment => payment.Id); entity.HasIndex(payment => payment.ExternalPaymentId).IsUnique().HasFilter("external_payment_id is not null"); entity.Property(payment => payment.TenantId).HasColumnName("tenant_id"); entity.Property(payment => payment.ClientId).HasColumnName("client_id"); entity.Property(payment => payment.OrderId).HasColumnName("order_id"); entity.Property(payment => payment.BookingId).HasColumnName("booking_id"); entity.Property(payment => payment.OriginalAmount).HasColumnName("original_amount"); entity.Property(payment => payment.DiscountAmount).HasColumnName("discount_amount"); entity.Property(payment => payment.PayableAmount).HasColumnName("payable_amount"); entity.Property(payment => payment.RefundedAmount).HasColumnName("refunded_amount"); entity.Property(payment => payment.ExternalPaymentId).HasColumnName("external_payment_id"); entity.Property(payment => payment.CreatedAt).HasColumnName("created_at"); });
        modelBuilder.Entity<TenantPaymentProviderEntity>(entity => { entity.ToTable("tenant_payment_providers"); entity.HasKey(provider => provider.Id); entity.HasIndex(provider => new { provider.TenantId, provider.Provider, provider.DisplayName }).IsUnique(); entity.Property(provider => provider.TenantId).HasColumnName("tenant_id"); entity.Property(provider => provider.EncryptedCredentials).HasColumnName("encrypted_credentials"); entity.Property(provider => provider.WebhookSecretHash).HasColumnName("webhook_secret_hash"); entity.Property(provider => provider.IsActive).HasColumnName("is_active"); entity.Property(provider => provider.CreatedAt).HasColumnName("created_at"); entity.Property(provider => provider.UpdatedAt).HasColumnName("updated_at"); });
    }
}

public sealed class PaymentEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public Guid ClientId { get; set; } public Guid? OrderId { get; set; } public Guid? BookingId { get; set; } public decimal OriginalAmount { get; set; } public decimal DiscountAmount { get; set; } public decimal PayableAmount { get; set; } public decimal RefundedAmount { get; set; } public string Currency { get; set; } = string.Empty; public string Provider { get; set; } = string.Empty; public string? Promocode { get; set; } public string? ExternalPaymentId { get; set; } public string Status { get; set; } = string.Empty; public string Type { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } }

public sealed class TenantPaymentProviderEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public string Provider { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string EncryptedCredentials { get; set; } = string.Empty; public string WebhookSecretHash { get; set; } = string.Empty; public bool IsActive { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
