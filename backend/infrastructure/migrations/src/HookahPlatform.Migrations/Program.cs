using HookahPlatform.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var connectionString = ResolveConnectionString(args);
var options = new DbContextOptionsBuilder<HookahPlatformMigrationDbContext>()
    .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly(typeof(HookahPlatformMigrationDbContext).Assembly.FullName))
    .Options;

for (var attempt = 1; ; attempt++)
{
    try
    {
        await using var db = new HookahPlatformMigrationDbContext(options);
        await db.Database.MigrateAsync();
        Console.WriteLine("Database migrations applied.");
        return;
    }
    catch (NpgsqlException) when (attempt < 30)
    {
        Console.WriteLine($"PostgreSQL is not ready yet. Retry {attempt}/30...");
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

static string ResolveConnectionString(string[] args)
{
    var fromArgs = args.FirstOrDefault(arg => arg.Contains("Host=", StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(fromArgs)) return fromArgs;

    return Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=hookah;Username=hookah;Password=hookah";
}
