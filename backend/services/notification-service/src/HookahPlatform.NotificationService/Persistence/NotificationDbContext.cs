using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HookahPlatform.NotificationService.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<NotificationTemplateEntity> Templates => Set<NotificationTemplateEntity>();
    public DbSet<NotificationPreferenceEntity> Preferences => Set<NotificationPreferenceEntity>();
    public DbSet<ProcessedIntegrationEventEntity> ProcessedEvents => Set<ProcessedIntegrationEventEntity>();
    public DbSet<TenantNotificationChannelEntity> TenantChannels => Set<TenantNotificationChannelEntity>();
    public DbSet<NotificationDeliveryEntity> Deliveries => Set<NotificationDeliveryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<NotificationEntity>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.TenantId).HasColumnName("tenant_id");
            entity.Property(notification => notification.UserId).HasColumnName("user_id");
            entity.Property(notification => notification.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            entity.Property(notification => notification.CreatedAt).HasColumnName("created_at");
            entity.Property(notification => notification.ReadAt).HasColumnName("read_at");
        });
        modelBuilder.Entity<NotificationTemplateEntity>(entity => { entity.ToTable("notification_templates"); entity.HasKey(template => template.Code); entity.Property(template => template.TenantId).HasColumnName("tenant_id"); });
        modelBuilder.Entity<NotificationPreferenceEntity>(entity => { entity.ToTable("notification_preferences"); entity.HasKey(preference => preference.UserId); entity.Property(preference => preference.TenantId).HasColumnName("tenant_id"); entity.Property(preference => preference.UserId).HasColumnName("user_id"); entity.Property(preference => preference.CrmEnabled).HasColumnName("crm_enabled"); entity.Property(preference => preference.TelegramEnabled).HasColumnName("telegram_enabled"); entity.Property(preference => preference.SmsEnabled).HasColumnName("sms_enabled"); entity.Property(preference => preference.EmailEnabled).HasColumnName("email_enabled"); entity.Property(preference => preference.PushEnabled).HasColumnName("push_enabled"); });
        modelBuilder.Entity<ProcessedIntegrationEventEntity>(entity => { entity.ToTable("processed_integration_events"); entity.HasKey(item => new { item.Handler, item.EventId }); entity.Property(item => item.EventId).HasColumnName("event_id"); entity.Property(item => item.ProcessedAt).HasColumnName("processed_at"); });
        modelBuilder.Entity<TenantNotificationChannelEntity>(entity => { entity.ToTable("tenant_notification_channels"); entity.HasKey(channel => channel.Id); entity.HasIndex(channel => new { channel.TenantId, channel.Channel }).IsUnique(); entity.Property(channel => channel.TenantId).HasColumnName("tenant_id"); entity.Property(channel => channel.EncryptedSettings).HasColumnName("encrypted_settings"); entity.Property(channel => channel.IsActive).HasColumnName("is_active"); entity.Property(channel => channel.CreatedAt).HasColumnName("created_at"); entity.Property(channel => channel.UpdatedAt).HasColumnName("updated_at"); });
        modelBuilder.Entity<NotificationDeliveryEntity>(entity => { entity.ToTable("notification_deliveries"); entity.HasKey(delivery => delivery.Id); entity.Property(delivery => delivery.TenantId).HasColumnName("tenant_id"); entity.Property(delivery => delivery.NotificationId).HasColumnName("notification_id"); entity.Property(delivery => delivery.ProviderMessageId).HasColumnName("provider_message_id"); entity.Property(delivery => delivery.CreatedAt).HasColumnName("created_at"); });
    }
}

public sealed class NotificationEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public Guid UserId { get; set; } public string Channel { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; public JsonDocument Metadata { get; set; } = JsonDocument.Parse("{}"); public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? ReadAt { get; set; } }
public sealed class NotificationTemplateEntity { public Guid TenantId { get; set; } public string Code { get; set; } = string.Empty; public string Channel { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; }
public sealed class NotificationPreferenceEntity { public Guid TenantId { get; set; } public Guid UserId { get; set; } public bool CrmEnabled { get; set; } public bool TelegramEnabled { get; set; } public bool SmsEnabled { get; set; } public bool EmailEnabled { get; set; } public bool PushEnabled { get; set; } }
public sealed class ProcessedIntegrationEventEntity { public string Handler { get; set; } = string.Empty; public Guid EventId { get; set; } public DateTimeOffset ProcessedAt { get; set; } }
public sealed class TenantNotificationChannelEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public string Channel { get; set; } = string.Empty; public string EncryptedSettings { get; set; } = string.Empty; public bool IsActive { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
public sealed class NotificationDeliveryEntity { public Guid Id { get; set; } public Guid TenantId { get; set; } public Guid? NotificationId { get; set; } public string Channel { get; set; } = string.Empty; public string Recipient { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public string? ProviderMessageId { get; set; } public string? Error { get; set; } public DateTimeOffset CreatedAt { get; set; } }
