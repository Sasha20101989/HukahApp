using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HookahPlatform.Migrations.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ReadEmbeddedSql("001_init.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop trigger if exists trg_mix_items_percent_sum_insert on mix_items;
            drop trigger if exists trg_mix_items_percent_sum_update on mix_items;
            drop trigger if exists trg_mix_items_percent_sum_delete on mix_items;
            drop function if exists ensure_mix_items_percent_sum() cascade;

            drop table if exists analytics_tobacco_usage cascade;
            drop table if exists analytics_bookings cascade;
            drop table if exists analytics_orders cascade;
            drop table if exists promocode_redemptions cascade;
            drop table if exists promocodes cascade;
            drop table if exists reviews cascade;
            drop table if exists notifications cascade;
            drop table if exists notification_preferences cascade;
            drop table if exists notification_templates cascade;
            drop table if exists payments cascade;
            drop table if exists inventory_movements cascade;
            drop table if exists coal_changes cascade;
            drop table if exists order_items cascade;
            drop table if exists orders cascade;
            drop table if exists bookings cascade;
            drop table if exists mix_items cascade;
            drop table if exists mixes cascade;
            drop table if exists inventory_items cascade;
            drop table if exists tobaccos cascade;
            drop table if exists bowls cascade;
            drop table if exists hookahs cascade;
            drop table if exists tables cascade;
            drop table if exists branch_working_hours cascade;
            drop table if exists zones cascade;
            drop table if exists halls cascade;
            drop table if exists staff_shifts cascade;
            drop table if exists refresh_tokens cascade;
            drop table if exists users cascade;
            drop table if exists branches cascade;
            drop table if exists role_permissions cascade;
            drop table if exists permissions cascade;
            drop table if exists roles cascade;
            drop table if exists processed_integration_events cascade;
            drop table if exists integration_outbox cascade;
            """);
    }

    private static string ReadEmbeddedSql(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded SQL resource '{fileName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
