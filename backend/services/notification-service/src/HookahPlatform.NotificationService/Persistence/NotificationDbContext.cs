using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.NotificationService.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<NotificationTemplateEntity> Templates => Set<NotificationTemplateEntity>();
    public DbSet<NotificationPreferenceEntity> Preferences => Set<NotificationPreferenceEntity>();
    public DbSet<ProcessedIntegrationEventEntity> ProcessedEvents => Set<ProcessedIntegrationEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<NotificationEntity>(entity => { entity.ToTable("notifications"); entity.HasKey(notification => notification.Id); entity.Property(notification => notification.UserId).HasColumnName("user_id"); entity.Property(notification => notification.CreatedAt).HasColumnName("created_at"); entity.Property(notification => notification.ReadAt).HasColumnName("read_at"); });
        modelBuilder.Entity<NotificationTemplateEntity>(entity => { entity.ToTable("notification_templates"); entity.HasKey(template => template.Code); });
        modelBuilder.Entity<NotificationPreferenceEntity>(entity => { entity.ToTable("notification_preferences"); entity.HasKey(preference => preference.UserId); entity.Property(preference => preference.UserId).HasColumnName("user_id"); entity.Property(preference => preference.CrmEnabled).HasColumnName("crm_enabled"); entity.Property(preference => preference.TelegramEnabled).HasColumnName("telegram_enabled"); entity.Property(preference => preference.SmsEnabled).HasColumnName("sms_enabled"); entity.Property(preference => preference.EmailEnabled).HasColumnName("email_enabled"); entity.Property(preference => preference.PushEnabled).HasColumnName("push_enabled"); });
        modelBuilder.Entity<ProcessedIntegrationEventEntity>(entity => { entity.ToTable("processed_integration_events"); entity.HasKey(item => new { item.Handler, item.EventId }); entity.Property(item => item.EventId).HasColumnName("event_id"); entity.Property(item => item.ProcessedAt).HasColumnName("processed_at"); });
    }
}

public sealed class NotificationEntity { public Guid Id { get; set; } public Guid UserId { get; set; } public string Channel { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; public string Metadata { get; set; } = "{}"; public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? ReadAt { get; set; } }
public sealed class NotificationTemplateEntity { public string Code { get; set; } = string.Empty; public string Channel { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; }
public sealed class NotificationPreferenceEntity { public Guid UserId { get; set; } public bool CrmEnabled { get; set; } public bool TelegramEnabled { get; set; } public bool SmsEnabled { get; set; } public bool EmailEnabled { get; set; } public bool PushEnabled { get; set; } }
public sealed class ProcessedIntegrationEventEntity { public string Handler { get; set; } = string.Empty; public Guid EventId { get; set; } public DateTimeOffset ProcessedAt { get; set; } }
