using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleRbacFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                alter table roles add column if not exists is_system boolean not null default false;
                alter table roles add column if not exists is_active boolean not null default true;

                do $$
                begin
                    if exists (
                        select 1
                        from information_schema.columns
                        where table_schema = current_schema()
                          and table_name = 'roles'
                          and column_name = 'tenant_id'
                    ) then
                        -- Only OWNER is a system role by requirement; other roles are tenant-managed.
                        update roles
                        set is_system = true
                        where tenant_id = '11111111-1111-1111-1111-111111111111'
                          and code = 'OWNER';

                        create index if not exists ix_roles_tenant_is_active on roles(tenant_id, is_active);
                    end if;
                end $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop index if exists ix_roles_tenant_is_active;
                alter table roles drop column if exists is_active;
                alter table roles drop column if exists is_system;
                """);
        }
    }
}
