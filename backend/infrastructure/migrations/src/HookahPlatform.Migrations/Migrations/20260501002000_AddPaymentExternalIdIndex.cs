using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations;

public partial class AddPaymentExternalIdIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create unique index if not exists ux_payments_external_payment_id
                on payments(external_payment_id)
                where external_payment_id is not null;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop index if exists ux_payments_external_payment_id;");
    }
}
