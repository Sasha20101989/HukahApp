using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.PaymentService.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentEntity>(entity => { entity.ToTable("payments"); entity.HasKey(payment => payment.Id); entity.Property(payment => payment.ClientId).HasColumnName("client_id"); entity.Property(payment => payment.OrderId).HasColumnName("order_id"); entity.Property(payment => payment.BookingId).HasColumnName("booking_id"); entity.Property(payment => payment.OriginalAmount).HasColumnName("original_amount"); entity.Property(payment => payment.DiscountAmount).HasColumnName("discount_amount"); entity.Property(payment => payment.PayableAmount).HasColumnName("payable_amount"); entity.Property(payment => payment.RefundedAmount).HasColumnName("refunded_amount"); entity.Property(payment => payment.ExternalPaymentId).HasColumnName("external_payment_id"); entity.Property(payment => payment.CreatedAt).HasColumnName("created_at"); });
    }
}

public sealed class PaymentEntity { public Guid Id { get; set; } public Guid ClientId { get; set; } public Guid? OrderId { get; set; } public Guid? BookingId { get; set; } public decimal OriginalAmount { get; set; } public decimal DiscountAmount { get; set; } public decimal PayableAmount { get; set; } public decimal RefundedAmount { get; set; } public string Currency { get; set; } = string.Empty; public string Provider { get; set; } = string.Empty; public string? Promocode { get; set; } public string? ExternalPaymentId { get; set; } public string Status { get; set; } = string.Empty; public string Type { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } }
