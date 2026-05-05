using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(HookahPlatformMigrationDbContext))]
    [Migration("20260505090000_AddAuditLogs")]
    public partial class AddAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create table if not exists audit_logs (
                    id uuid primary key,
                    tenant_id uuid null references tenants(id),
                    actor_user_id uuid null,
                    action varchar(120) not null,
                    target_type varchar(120) not null,
                    target_id varchar(120) null,
                    result varchar(40) not null,
                    correlation_id varchar(120) null,
                    metadata_json jsonb null,
                    created_at timestamptz not null
                );
                create index if not exists ix_audit_logs_tenant_created_at on audit_logs(tenant_id, created_at desc);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop index if exists ix_audit_logs_tenant_created_at;
                drop table if exists audit_logs;
                """);
        }
    }
}
