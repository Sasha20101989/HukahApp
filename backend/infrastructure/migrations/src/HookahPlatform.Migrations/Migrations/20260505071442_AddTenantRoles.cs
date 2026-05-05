using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRoles : Migration
    {
        private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "roles",
                type: "uuid",
                nullable: false,
                defaultValue: DemoTenantId);

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id",
                table: "roles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_roles_tenant_code",
                table: "roles",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ux_roles_tenant_code", table: "roles");
            migrationBuilder.DropIndex(name: "ix_roles_tenant_id", table: "roles");
            migrationBuilder.DropColumn(name: "tenant_id", table: "roles");
        }
    }
}
