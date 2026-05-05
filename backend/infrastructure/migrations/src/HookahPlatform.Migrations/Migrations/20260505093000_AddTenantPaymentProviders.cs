using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(HookahPlatformMigrationDbContext))]
    [Migration("20260505093000_AddTenantPaymentProviders")]
    public partial class AddTenantPaymentProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create table if not exists tenant_payment_providers (
                    id uuid primary key,
                    tenant_id uuid not null references tenants(id),
                    provider varchar(80) not null,
                    display_name varchar(160) not null,
                    encrypted_credentials text not null,
                    webhook_secret_hash text not null,
                    is_active boolean not null,
                    created_at timestamptz not null,
                    updated_at timestamptz not null
                );
                create unique index if not exists ux_tenant_payment_provider on tenant_payment_providers(tenant_id, provider, display_name);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop index if exists ux_tenant_payment_provider;
                drop table if exists tenant_payment_providers;
                """);
        }
    }
}
