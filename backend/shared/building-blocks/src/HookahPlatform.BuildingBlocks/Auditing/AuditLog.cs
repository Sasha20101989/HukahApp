using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.BuildingBlocks.Security;
using HookahPlatform.BuildingBlocks.Tenancy;
using HookahPlatform.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HookahPlatform.BuildingBlocks.Auditing;

public interface IAuditLogWriter
{
    Task WriteAsync(Guid? tenantId, Guid? actorUserId, string action, string targetType, string? targetId, string result, string? correlationId, object? metadata, CancellationToken cancellationToken = default);
}

public sealed class PostgresAuditLogWriter(OutboxDbContext db) : IAuditLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task WriteAsync(Guid? tenantId, Guid? actorUserId, string action, string targetType, string? targetId, string result, string? correlationId, object? metadata, CancellationToken cancellationToken = default)
    {
        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into audit_logs(id, tenant_id, actor_user_id, action, target_type, target_id, result, correlation_id, metadata_json, created_at)
            values ({Guid.NewGuid()}, {tenantId}, {actorUserId}, {action}, {targetType}, {targetId}, {result}, {correlationId}, {metadataJson}::jsonb, {DateTimeOffset.UtcNow})
            """, cancellationToken);
    }
}

public static class AuditLogContext
{
    public static Guid? ForwardedUserId(HttpContext context)
    {
        return Guid.TryParse(context.Request.Headers[ServiceAccessControl.UserIdHeader].ToString(), out var userId) ? userId : null;
    }

    public static Guid? TenantId(ITenantContext tenantContext)
    {
        return tenantContext.TenantId ?? TenantConstants.DemoTenantId;
    }

    public static string? CorrelationId(HttpContext context)
    {
        return context.Request.Headers[Observability.CorrelationIdHeader].FirstOrDefault()
            ?? System.Diagnostics.Activity.Current?.TraceId.ToString();
    }
}
