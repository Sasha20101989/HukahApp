using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HookahPlatform.Migrations;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<HookahPlatformMigrationDbContext>
{
    public HookahPlatformMigrationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hookah;Username=hookah;Password=hookah";

        var options = new DbContextOptionsBuilder<HookahPlatformMigrationDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly(typeof(HookahPlatformMigrationDbContext).Assembly.FullName))
            .Options;

        return new HookahPlatformMigrationDbContext(options);
    }
}
