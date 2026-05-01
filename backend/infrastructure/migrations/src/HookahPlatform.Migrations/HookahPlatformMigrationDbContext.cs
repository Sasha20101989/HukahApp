using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.Migrations;

public sealed class HookahPlatformMigrationDbContext(DbContextOptions<HookahPlatformMigrationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.HasPostgresExtension("btree_gist");
    }
}
