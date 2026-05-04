using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations;

public partial class AddTenantIdColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Phase 1: introduce tenant_id columns with a safe default (demo tenant) to avoid breaking existing data.
        // Later phases will remove defaults and enforce strict tenant scoping on writes.
        migrationBuilder.Sql("""
            do $$
            begin
                if not exists (select 1 from information_schema.columns where table_name='branches' and column_name='tenant_id') then
                    alter table branches add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_branches_tenant_id on branches(tenant_id);
                    alter table branches add constraint fk_branches_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='users' and column_name='tenant_id') then
                    alter table users add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_users_tenant_id on users(tenant_id);
                    alter table users add constraint fk_users_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='halls' and column_name='tenant_id') then
                    alter table halls add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_halls_tenant_id on halls(tenant_id);
                    alter table halls add constraint fk_halls_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='zones' and column_name='tenant_id') then
                    alter table zones add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_zones_tenant_id on zones(tenant_id);
                    alter table zones add constraint fk_zones_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='tables' and column_name='tenant_id') then
                    alter table tables add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_tables_tenant_id on tables(tenant_id);
                    alter table tables add constraint fk_tables_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='hookahs' and column_name='tenant_id') then
                    alter table hookahs add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_hookahs_tenant_id on hookahs(tenant_id);
                    alter table hookahs add constraint fk_hookahs_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='inventory_items' and column_name='tenant_id') then
                    alter table inventory_items add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_inventory_items_tenant_id on inventory_items(tenant_id);
                    alter table inventory_items add constraint fk_inventory_items_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='inventory_movements' and column_name='tenant_id') then
                    alter table inventory_movements add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_inventory_movements_tenant_id on inventory_movements(tenant_id);
                    alter table inventory_movements add constraint fk_inventory_movements_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='mixes' and column_name='tenant_id') then
                    alter table mixes add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_mixes_tenant_id on mixes(tenant_id);
                    alter table mixes add constraint fk_mixes_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='mix_items' and column_name='tenant_id') then
                    alter table mix_items add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_mix_items_tenant_id on mix_items(tenant_id);
                    alter table mix_items add constraint fk_mix_items_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='bookings' and column_name='tenant_id') then
                    alter table bookings add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_bookings_tenant_id on bookings(tenant_id);
                    alter table bookings add constraint fk_bookings_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='orders' and column_name='tenant_id') then
                    alter table orders add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_orders_tenant_id on orders(tenant_id);
                    alter table orders add constraint fk_orders_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='order_items' and column_name='tenant_id') then
                    alter table order_items add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_order_items_tenant_id on order_items(tenant_id);
                    alter table order_items add constraint fk_order_items_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='payments' and column_name='tenant_id') then
                    alter table payments add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_payments_tenant_id on payments(tenant_id);
                    alter table payments add constraint fk_payments_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='reviews' and column_name='tenant_id') then
                    alter table reviews add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_reviews_tenant_id on reviews(tenant_id);
                    alter table reviews add constraint fk_reviews_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='promocodes' and column_name='tenant_id') then
                    alter table promocodes add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_promocodes_tenant_id on promocodes(tenant_id);
                    alter table promocodes add constraint fk_promocodes_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='promocode_redemptions' and column_name='tenant_id') then
                    alter table promocode_redemptions add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_promocode_redemptions_tenant_id on promocode_redemptions(tenant_id);
                    alter table promocode_redemptions add constraint fk_promocode_redemptions_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='notifications' and column_name='tenant_id') then
                    alter table notifications add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_notifications_tenant_id on notifications(tenant_id);
                    alter table notifications add constraint fk_notifications_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='notification_templates' and column_name='tenant_id') then
                    alter table notification_templates add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_notification_templates_tenant_id on notification_templates(tenant_id);
                    alter table notification_templates add constraint fk_notification_templates_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='notification_preferences' and column_name='tenant_id') then
                    alter table notification_preferences add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_notification_preferences_tenant_id on notification_preferences(tenant_id);
                    alter table notification_preferences add constraint fk_notification_preferences_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;

                if not exists (select 1 from information_schema.columns where table_name='staff_shifts' and column_name='tenant_id') then
                    alter table staff_shifts add column tenant_id uuid not null default '11111111-1111-1111-1111-111111111111';
                    create index if not exists ix_staff_shifts_tenant_id on staff_shifts(tenant_id);
                    alter table staff_shifts add constraint fk_staff_shifts_tenant foreign key (tenant_id) references tenants(id) on delete restrict;
                end if;
            end $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Down is best-effort; remove tenant columns but keep tenant tables.
        migrationBuilder.Sql("""
            alter table if exists staff_shifts drop column if exists tenant_id;
            alter table if exists notification_preferences drop column if exists tenant_id;
            alter table if exists notification_templates drop column if exists tenant_id;
            alter table if exists notifications drop column if exists tenant_id;
            alter table if exists promocode_redemptions drop column if exists tenant_id;
            alter table if exists promocodes drop column if exists tenant_id;
            alter table if exists reviews drop column if exists tenant_id;
            alter table if exists payments drop column if exists tenant_id;
            alter table if exists order_items drop column if exists tenant_id;
            alter table if exists orders drop column if exists tenant_id;
            alter table if exists bookings drop column if exists tenant_id;
            alter table if exists mix_items drop column if exists tenant_id;
            alter table if exists mixes drop column if exists tenant_id;
            alter table if exists inventory_movements drop column if exists tenant_id;
            alter table if exists inventory_items drop column if exists tenant_id;
            alter table if exists hookahs drop column if exists tenant_id;
            alter table if exists tables drop column if exists tenant_id;
            alter table if exists zones drop column if exists tenant_id;
            alter table if exists halls drop column if exists tenant_id;
            alter table if exists users drop column if exists tenant_id;
            alter table if exists branches drop column if exists tenant_id;
            """);
    }
}

