using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations;

public partial class AddZoneLayout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            alter table zones add column if not exists x_position numeric(10, 2) not null default 40;
            alter table zones add column if not exists y_position numeric(10, 2) not null default 40;
            alter table zones add column if not exists width numeric(10, 2) not null default 360;
            alter table zones add column if not exists height numeric(10, 2) not null default 220;
            update zones
            set x_position = 30, y_position = 40, width = 460, height = 280
            where id = '21000000-0000-0000-0000-000000000001';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            alter table zones drop column if exists height;
            alter table zones drop column if exists width;
            alter table zones drop column if exists y_position;
            alter table zones drop column if exists x_position;
            """);
    }
}
