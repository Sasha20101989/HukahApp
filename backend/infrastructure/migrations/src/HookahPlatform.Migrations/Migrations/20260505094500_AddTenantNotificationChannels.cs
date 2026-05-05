using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(HookahPlatformMigrationDbContext))]
    [Migration("20260505094500_AddTenantNotificationChannels")]
    public partial class AddTenantNotificationChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create table if not exists tenant_notification_channels (
                    id uuid primary key,
                    tenant_id uuid not null references tenants(id),
                    channel varchar(40) not null,
                    encrypted_settings text not null,
                    is_active boolean not null,
                    created_at timestamptz not null,
                    updated_at timestamptz not null
                );
                create unique index if not exists ux_tenant_notification_channel on tenant_notification_channels(tenant_id, channel);

                create table if not exists notification_deliveries (
                    id uuid primary key,
                    tenant_id uuid not null references tenants(id),
                    notification_id uuid null,
                    channel varchar(40) not null,
                    recipient varchar(255) not null,
                    status varchar(40) not null,
                    provider_message_id varchar(255) null,
                    error text null,
                    created_at timestamptz not null
                );
                create index if not exists ix_notification_deliveries_tenant_created_at on notification_deliveries(tenant_id, created_at desc);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop index if exists ix_notification_deliveries_tenant_created_at;
                drop table if exists notification_deliveries;
                drop index if exists ux_tenant_notification_channel;
                drop table if exists tenant_notification_channels;
                """);
        }
    }
}
