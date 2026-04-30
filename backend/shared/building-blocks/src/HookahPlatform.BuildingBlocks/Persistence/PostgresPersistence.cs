using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HookahPlatform.BuildingBlocks.Persistence;

public static class PostgresPersistence
{
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=hookah;Username=hookah;Password=hookah";

    public static WebApplicationBuilder AddPostgresDbContext<TContext>(this WebApplicationBuilder builder)
        where TContext : DbContext
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres")
            ?? builder.Configuration["ConnectionStrings:Postgres"]
            ?? DefaultConnectionString;

        builder.Services.AddDbContext<TContext>(options => options.UseNpgsql(connectionString));
        return builder;
    }

    public static WebApplication MapPersistenceHealth<TContext>(this WebApplication app, string serviceName)
        where TContext : DbContext
    {
        app.MapGet("/persistence/health", async (TContext db, CancellationToken cancellationToken) =>
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return Results.Ok(new
            {
                service = serviceName,
                provider = db.Database.ProviderName,
                canConnect
            });
        });

        return app;
    }
}
