# Database Migrations

The platform uses a single managed EF Core migration project because the current backend services share one PostgreSQL database.

Project:

- `backend/infrastructure/migrations/src/HookahPlatform.Migrations`

Runtime migrator:

```bash
dotnet run --project backend/infrastructure/migrations/src/HookahPlatform.Migrations/HookahPlatform.Migrations.csproj
```

Connection string resolution order:

- first CLI argument that contains `Host=`
- `ConnectionStrings__Postgres`
- `POSTGRES_CONNECTION_STRING`
- local default `Host=localhost;Port=5432;Database=hookah;Username=hookah;Password=hookah`

Docker Compose runs `db-migrator` after PostgreSQL is healthy and before backend services start.

The initial migration `20260501000000_InitialCreate` embeds the previous SQL baseline so existing schema, indexes, triggers, seed data and PostgreSQL extensions are now tracked through EF migration history.

To add a new migration:

```bash
dotnet ef migrations add <Name> \
  --project backend/infrastructure/migrations/src/HookahPlatform.Migrations/HookahPlatform.Migrations.csproj
```

For this shared-database setup, do not add migrations inside individual service projects. Keep schema evolution centralized in `HookahPlatform.Migrations` to avoid conflicting ownership of shared tables such as `users`, `payments`, `integration_outbox` and `processed_integration_events`.
