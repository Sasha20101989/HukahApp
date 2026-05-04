using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations;

public partial class AddTenantFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create table if not exists tenants (
                id uuid primary key,
                slug varchar(80) not null,
                name varchar(200) not null,
                is_active boolean not null default true,
                created_at timestamptz not null default now()
            );

            create unique index if not exists ux_tenants_slug on tenants(slug);

            create table if not exists tenant_settings (
                tenant_id uuid primary key references tenants(id) on delete cascade,
                default_timezone varchar(80) not null default 'Europe/Moscow',
                default_currency varchar(8) not null default 'RUB',
                require_deposit boolean not null default false
            );
            """);

        // Seed a demo tenant for local/dev. Production will create tenants through onboarding.
        migrationBuilder.Sql("""
            insert into tenants(id, slug, name, is_active)
            values ('11111111-1111-1111-1111-111111111111', 'demo', 'Demo Tenant', true)
            on conflict (id) do nothing;

            insert into tenant_settings(tenant_id, default_timezone, default_currency, require_deposit)
            values ('11111111-1111-1111-1111-111111111111', 'Europe/Moscow', 'RUB', false)
            on conflict (tenant_id) do nothing;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop table if exists tenant_settings cascade;
            drop table if exists tenants cascade;
            """);
    }
}

