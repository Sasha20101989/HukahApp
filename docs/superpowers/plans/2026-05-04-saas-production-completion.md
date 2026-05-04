# SaaS Production Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Hookah CRM Platform from the current executable platform slice to a production-ready multi-tenant SaaS in controlled phases.

**Architecture:** Introduce tenant context as a cross-cutting platform primitive, then enforce tenant isolation through gateway, services, PostgreSQL, Redis, RabbitMQ, frontend, and tests. Keep the existing service boundaries, centralized migrations, pnpm frontend workspace, and Docker Compose local workflow while adding Kubernetes production deployment as a later phase.

**Tech Stack:** ASP.NET Core 8, EF Core/Npgsql, PostgreSQL, Redis, RabbitMQ, Next.js, TypeScript, pnpm, Docker Compose, Kubernetes/Helm, GitHub Actions, OpenTelemetry, Serilog.

---

## Execution Rules

- Execute phases in order.
- Do not start Phase 2 until Phase 1 tenant isolation tests pass.
- Keep commits small and phase-scoped.
- Keep the current dirty worktree safe: never revert unrelated changes.
- Every task must end with build/test commands listed in that task.
- If a task discovers a mismatch between code and this plan, update this plan before continuing.

## Master File Map

### Shared Backend

- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/EndpointAccessPolicy.cs`
  - Keeps endpoint permission matrix tenant-aware.
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/DomainEvents.cs`
  - Adds tenant metadata to integration event contracts.
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/MessagingCatalog.cs`
  - Keeps tenant-aware routing metadata explicit.
- Create: `backend/shared/contracts/src/HookahPlatform.Contracts/TenantContracts.cs`
  - Defines tenant DTOs, tenant settings DTOs, and tenant headers.
- Create: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantContext.cs`
  - Holds current tenant id and resolution metadata.
- Create: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantMiddleware.cs`
  - Reads and validates `X-Tenant-Id` for service requests.
- Create: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantDbContextExtensions.cs`
  - Provides EF tenant filter helpers and tenant write validation helpers.
- Modify: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/ApiDefaults.cs`
  - Registers tenant middleware, tenant services, rate-limit hooks, and secure headers.
- Modify: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Observability.cs`
  - Adds tenant and correlation tags to logs/traces/metrics.

### Tenant Foundation

- Create: `backend/services/tenant-service/src/HookahPlatform.TenantService/HookahPlatform.TenantService.csproj`
- Create: `backend/services/tenant-service/src/HookahPlatform.TenantService/Program.cs`
- Create: `backend/services/tenant-service/src/HookahPlatform.TenantService/Persistence/TenantDbContext.cs`
- Modify: `backend/HookahPlatform.sln`
- Modify: `infrastructure/docker-compose.yml`
- Modify: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/HookahPlatform.Migrations.csproj`
- Create: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/Migrations/20260504010000_AddTenantFoundation.cs`

### Services To Tenant-Scope

- Modify: `backend/services/auth-service/src/HookahPlatform.AuthService/Program.cs`
- Modify: `backend/services/user-service/src/HookahPlatform.UserService/Program.cs`
- Modify: `backend/services/branch-service/src/HookahPlatform.BranchService/Program.cs`
- Modify: `backend/services/mixology-service/src/HookahPlatform.MixologyService/Program.cs`
- Modify: `backend/services/inventory-service/src/HookahPlatform.InventoryService/Program.cs`
- Modify: `backend/services/order-service/src/HookahPlatform.OrderService/Program.cs`
- Modify: `backend/services/booking-service/src/HookahPlatform.BookingService/Program.cs`
- Modify: `backend/services/payment-service/src/HookahPlatform.PaymentService/Program.cs`
- Modify: `backend/services/notification-service/src/HookahPlatform.NotificationService/Program.cs`
- Modify: `backend/services/analytics-service/src/HookahPlatform.AnalyticsService/Program.cs`
- Modify: `backend/services/review-service/src/HookahPlatform.ReviewService/Program.cs`
- Modify: `backend/services/promo-service/src/HookahPlatform.PromoService/Program.cs`

### Frontend

- Modify: `frontend/crm-app/lib/api.ts`
- Modify: `frontend/crm-app/lib/store.ts`
- Modify: `frontend/crm-app/app/page.tsx`
- Create: `frontend/crm-app/app/admin/tenants/page.tsx`
- Create: `frontend/crm-app/app/admin/roles/page.tsx`
- Modify: `frontend/client-app/lib/api.ts`
- Modify: `frontend/client-app/lib/store.ts`
- Modify: `frontend/client-app/app/page.tsx`

### Infrastructure And Testing

- Modify: `.github/workflows/backend.yml`
- Modify: `.github/workflows/frontend.yml`
- Create: `.github/workflows/container-images.yml`
- Create: `.github/workflows/deploy-staging.yml`
- Create: `infrastructure/helm/hookah-platform/Chart.yaml`
- Create: `infrastructure/helm/hookah-platform/values.yaml`
- Create: `infrastructure/helm/hookah-platform/templates/deployment.yaml`, `service.yaml`, `ingress.yaml`, `migrations-job.yaml`, `_helpers.tpl`
- Modify: `scripts/api-crud-smoke.mjs`
- Modify: `scripts/frontend-smoke.mjs`
- Modify: `scripts/browser-smoke.mjs`
- Create: `scripts/tenant-isolation-smoke.mjs`
- Create: `docs/tenant-model.md`
- Create: `docs/production-readiness.md`
- Modify: `docs/api.md`
- Modify: `docs/architecture.md`
- Modify: `docs/database.md`
- Modify: `docs/events.md`
- Modify: `docs/frontend.md`
- Modify: `docs/observability.md`

---

## Phase 0 - Stabilize Current Platform

### Task 0.1: Baseline Build And Smoke Inventory

**Files:**
- Modify: `docs/production-readiness.md`

- [ ] **Step 1: Create production readiness inventory**

Create `docs/production-readiness.md` with this content:

```markdown
# Production Readiness

## Baseline Commands

- Backend build: `dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false`
- Frontend build: `corepack pnpm frontend:build`
- API smoke: `corepack pnpm api:crud-smoke`
- Frontend HTTP smoke: `corepack pnpm frontend:smoke`
- Browser smoke: `corepack pnpm frontend:browser-smoke`
- Local stack: `corepack pnpm local:up`

## Production Gates

- No `package-lock.json` in the pnpm workspace.
- Docker Compose starts the full stack locally.
- Backend build passes.
- Frontend build passes.
- API smoke passes against a running gateway.
- Frontend smoke passes against running CRM and client apps.
- Browser smoke passes against running CRM and client apps.

## Current Known Pre-SaaS Gaps

- Tenant model is not first-class.
- Tenant isolation tests do not exist.
- Kubernetes manifests are not production complete.
- CI/CD does not yet publish and deploy all service images.
```

- [ ] **Step 2: Run backend build**

Run:

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
```

Expected: `Build succeeded`.

- [ ] **Step 3: Run frontend build**

Run:

```bash
corepack pnpm frontend:build
```

Expected: both CRM and client Next.js builds complete successfully.

- [ ] **Step 4: Check lockfile policy**

Run:

```bash
find . -name package-lock.json -print
```

Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add docs/production-readiness.md
git commit -m "docs: add production readiness gates"
```

### Task 0.2: Normalize Local Startup Verification

**Files:**
- Modify: `README.md`
- Modify: `scripts/local-up.sh`

- [ ] **Step 1: Inspect local startup script**

Run:

```bash
sed -n '1,240p' scripts/local-up.sh
```

Expected: script documents compose file, project name, reset/logs behavior, and exits non-zero on compose failure.

- [ ] **Step 2: Update README local startup section**

Ensure `README.md` includes this exact command block:

```markdown
## Run Full Platform Locally

```bash
corepack pnpm local:up
```

With logs:

```bash
corepack pnpm local:up:logs
```

Reset local containers and volumes:

```bash
corepack pnpm local:reset
```
```

- [ ] **Step 3: Run local stack config validation**

Run:

```bash
docker compose -p hookah-platform -f infrastructure/docker-compose.yml config >/tmp/hookah-compose.config.yml
```

Expected: command exits with code `0` and writes `/tmp/hookah-compose.config.yml`.

- [ ] **Step 4: Commit**

```bash
git add README.md scripts/local-up.sh
git commit -m "docs: document local platform startup"
```

### Task 0.3: Frontend Module Decomposition Preparation

**Files:**
- Create: `frontend/crm-app/app/sections/README.md`
- Create: `frontend/crm-app/app/components/README.md`
- Modify: `docs/frontend.md`

- [ ] **Step 1: Create CRM sections README**

Create `frontend/crm-app/app/sections/README.md`:

```markdown
# CRM Sections

This directory is the target home for CRM domain sections currently implemented in `app/page.tsx`.

Migration order:

1. Floor and branch setup.
2. Orders and bookings.
3. Inventory and mixology.
4. Staff, notifications, reviews, promo.
5. Analytics.

Each section should export one client component and keep API calls in `lib/api.ts`.
```

- [ ] **Step 2: Create CRM components README**

Create `frontend/crm-app/app/components/README.md`:

```markdown
# CRM Components

Shared CRM UI components belong here when they are specific to the CRM app.

Reusable cross-app primitives belong in `frontend/crm-app/lib/ui.tsx` or a future shared frontend package.
```

- [ ] **Step 3: Document decomposition target**

Append to `docs/frontend.md`:

```markdown
## CRM Decomposition Target

The current CRM dashboard can remain operational while it is split into focused client components. New production work should avoid adding more domain logic to `frontend/crm-app/app/page.tsx`; move new domain UI into `frontend/crm-app/app/sections`.
```

- [ ] **Step 4: Run frontend build**

```bash
corepack pnpm --dir frontend/crm-app build
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add frontend/crm-app/app/sections/README.md frontend/crm-app/app/components/README.md docs/frontend.md
git commit -m "docs: define CRM decomposition target"
```

---

## Phase 1 - Tenant Foundation

### Task 1.1: Add Tenant Contracts And Context

**Files:**
- Create: `backend/shared/contracts/src/HookahPlatform.Contracts/TenantContracts.cs`
- Create: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantContext.cs`
- Create: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantHeaders.cs`

- [ ] **Step 1: Add tenant contracts**

Create `backend/shared/contracts/src/HookahPlatform.Contracts/TenantContracts.cs`:

```csharp
namespace HookahPlatform.Contracts;

public static class TenantHeaderNames
{
    public const string TenantId = "X-Tenant-Id";
    public const string TenantSlug = "X-Tenant-Slug";
}

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string? PrimaryDomain,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TenantSettingsDto(
    Guid TenantId,
    string Timezone,
    string Currency,
    bool PaymentsEnabled,
    bool NotificationsEnabled);

public sealed record CreateTenantRequest(
    string Name,
    string Slug,
    string Timezone,
    string Currency);

public sealed record UpdateTenantRequest(
    string Name,
    string? PrimaryDomain,
    string Status);
```

- [ ] **Step 2: Add tenant context**

Create `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantContext.cs`:

```csharp
namespace HookahPlatform.BuildingBlocks.Tenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? TenantSlug { get; }
    bool HasTenant { get; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public string? TenantSlug { get; private set; }
    public bool HasTenant => TenantId.HasValue;

    public void Set(Guid tenantId, string? tenantSlug)
    {
        TenantId = tenantId;
        TenantSlug = string.IsNullOrWhiteSpace(tenantSlug) ? null : tenantSlug.Trim();
    }
}
```

- [ ] **Step 3: Add tenant header constants for building blocks**

Create `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantHeaders.cs`:

```csharp
namespace HookahPlatform.BuildingBlocks.Tenancy;

public static class TenantHeaders
{
    public const string TenantId = "X-Tenant-Id";
    public const string TenantSlug = "X-Tenant-Slug";
}
```

- [ ] **Step 4: Build contracts and building blocks**

```bash
dotnet build backend/shared/contracts/src/HookahPlatform.Contracts/HookahPlatform.Contracts.csproj --no-restore
dotnet build backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/HookahPlatform.BuildingBlocks.csproj --no-restore
```

Expected: both builds succeed.

- [ ] **Step 5: Commit**

```bash
git add backend/shared/contracts/src/HookahPlatform.Contracts/TenantContracts.cs backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantContext.cs backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantHeaders.cs
git commit -m "feat: add tenant contracts and context"
```

### Task 1.2: Add Tenant Middleware To Building Blocks

**Files:**
- Create: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantMiddleware.cs`
- Modify: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/ApiDefaults.cs`

- [ ] **Step 1: Add middleware**

Create `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantMiddleware.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace HookahPlatform.BuildingBlocks.Tenancy;

public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenantContext)
    {
        if (httpContext.Request.Headers.TryGetValue(TenantHeaders.TenantId, out var tenantValues)
            && Guid.TryParse(tenantValues.FirstOrDefault(), out var tenantId))
        {
            var slug = httpContext.Request.Headers.TryGetValue(TenantHeaders.TenantSlug, out var slugValues)
                ? slugValues.FirstOrDefault()
                : null;
            tenantContext.Set(tenantId, slug);
        }

        await _next(httpContext);
    }
}
```

- [ ] **Step 2: Register tenant services in ApiDefaults**

In `ApiDefaults.cs`, add:

```csharp
using HookahPlatform.BuildingBlocks.Tenancy;
```

Inside the shared service registration method, add:

```csharp
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
```

Inside the shared application middleware method before endpoint mapping, add:

```csharp
app.UseMiddleware<TenantMiddleware>();
```

- [ ] **Step 3: Build solution**

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Tenancy/TenantMiddleware.cs backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/ApiDefaults.cs
git commit -m "feat: register tenant middleware"
```

### Task 1.3: Add Tenant Service Skeleton

**Files:**
- Create: `backend/services/tenant-service/src/HookahPlatform.TenantService/HookahPlatform.TenantService.csproj`
- Create: `backend/services/tenant-service/src/HookahPlatform.TenantService/Program.cs`
- Create: `backend/services/tenant-service/src/HookahPlatform.TenantService/Persistence/TenantDbContext.cs`
- Modify: `backend/HookahPlatform.sln`

- [ ] **Step 1: Create tenant service project**

Create `backend/services/tenant-service/src/HookahPlatform.TenantService/HookahPlatform.TenantService.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../shared/contracts/src/HookahPlatform.Contracts/HookahPlatform.Contracts.csproj" />
    <ProjectReference Include="../../../../shared/building-blocks/src/HookahPlatform.BuildingBlocks/HookahPlatform.BuildingBlocks.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.11" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.11" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create TenantDbContext**

Create `backend/services/tenant-service/src/HookahPlatform.TenantService/Persistence/TenantDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.TenantService.Persistence;

public sealed class TenantDbContext : DbContext
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options) { }

    public DbSet<TenantRecord> Tenants => Set<TenantRecord>();
    public DbSet<TenantSettingsRecord> TenantSettings => Set<TenantSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantRecord>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
            entity.Property(x => x.PrimaryDomain).HasColumnName("primary_domain").HasMaxLength(255);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<TenantSettingsRecord>(entity =>
        {
            entity.ToTable("tenant_settings");
            entity.HasKey(x => x.TenantId);
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            entity.Property(x => x.PaymentsEnabled).HasColumnName("payments_enabled").IsRequired();
            entity.Property(x => x.NotificationsEnabled).HasColumnName("notifications_enabled").IsRequired();
            entity.HasOne<TenantRecord>().WithOne().HasForeignKey<TenantSettingsRecord>(x => x.TenantId);
        });
    }
}

public sealed class TenantRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Status { get; set; } = "ACTIVE";
    public string? PrimaryDomain { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TenantSettingsRecord
{
    public Guid TenantId { get; set; }
    public string Timezone { get; set; } = "Europe/Moscow";
    public string Currency { get; set; } = "RUB";
    public bool PaymentsEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
}
```

- [ ] **Step 3: Create tenant service Program**

Create `backend/services/tenant-service/src/HookahPlatform.TenantService/Program.cs`:

```csharp
using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;
using HookahPlatform.TenantService.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahApiDefaults("tenant-service");
builder.Services.AddDbContext<TenantDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var app = builder.Build();
app.UseHookahApiDefaults();

app.MapGet("/api/tenants", async (TenantDbContext db) =>
{
    var tenants = await db.Tenants
        .OrderBy(x => x.Name)
        .Select(x => new TenantDto(x.Id, x.Name, x.Slug, x.Status, x.PrimaryDomain, x.CreatedAt, x.UpdatedAt))
        .ToListAsync();
    return Results.Ok(tenants);
});

app.MapGet("/api/tenants/{id:guid}", async (Guid id, TenantDbContext db) =>
{
    var tenant = await db.Tenants.Where(x => x.Id == id)
        .Select(x => new TenantDto(x.Id, x.Name, x.Slug, x.Status, x.PrimaryDomain, x.CreatedAt, x.UpdatedAt))
        .FirstOrDefaultAsync();
    return tenant is null ? Results.NotFound() : Results.Ok(tenant);
});

app.MapPost("/api/tenants", async (CreateTenantRequest request, TenantDbContext db) =>
{
    var now = DateTimeOffset.UtcNow;
    var tenant = new TenantRecord
    {
        Id = Guid.NewGuid(),
        Name = request.Name.Trim(),
        Slug = request.Slug.Trim().ToLowerInvariant(),
        Status = "ACTIVE",
        CreatedAt = now,
        UpdatedAt = now
    };
    db.Tenants.Add(tenant);
    db.TenantSettings.Add(new TenantSettingsRecord
    {
        TenantId = tenant.Id,
        Timezone = request.Timezone.Trim(),
        Currency = request.Currency.Trim().ToUpperInvariant(),
        PaymentsEnabled = true,
        NotificationsEnabled = true
    });
    await db.SaveChangesAsync();
    return Results.Created($"/api/tenants/{tenant.Id}", new TenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.PrimaryDomain, tenant.CreatedAt, tenant.UpdatedAt));
});

app.MapGet("/persistence/health", async (TenantDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("database unavailable"));

app.Run();
```

- [ ] **Step 4: Add project to solution**

Run:

```bash
dotnet sln backend/HookahPlatform.sln add backend/services/tenant-service/src/HookahPlatform.TenantService/HookahPlatform.TenantService.csproj
```

Expected: solution reports project added.

- [ ] **Step 5: Build solution**

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
```

Expected: build succeeds after restore if needed.

- [ ] **Step 6: Commit**

```bash
git add backend/HookahPlatform.sln backend/services/tenant-service
git commit -m "feat: add tenant service skeleton"
```

### Task 1.4: Add Tenant Foundation Migration

**Files:**
- Create: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/Migrations/20260504010000_AddTenantFoundation.cs`
- Modify: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/HookahPlatformMigrationsDbContext.cs`
- Modify: `docs/database.md`

- [ ] **Step 1: Add migration operations**

Create an EF migration that performs these SQL operations:

```sql
CREATE TABLE IF NOT EXISTS tenants (
    id uuid PRIMARY KEY,
    name varchar(200) NOT NULL,
    slug varchar(120) NOT NULL UNIQUE,
    status varchar(40) NOT NULL,
    primary_domain varchar(255) NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS tenant_settings (
    tenant_id uuid PRIMARY KEY REFERENCES tenants(id) ON DELETE CASCADE,
    timezone varchar(80) NOT NULL,
    currency varchar(3) NOT NULL,
    payments_enabled boolean NOT NULL,
    notifications_enabled boolean NOT NULL
);

INSERT INTO tenants (id, name, slug, status, primary_domain, created_at, updated_at)
VALUES ('00000000-0000-0000-0000-000000000001', 'Demo Tenant', 'demo', 'ACTIVE', NULL, now(), now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO tenant_settings (tenant_id, timezone, currency, payments_enabled, notifications_enabled)
VALUES ('00000000-0000-0000-0000-000000000001', 'Europe/Moscow', 'RUB', true, true)
ON CONFLICT (tenant_id) DO NOTHING;
```

- [ ] **Step 2: Add tenant_id to tenant-owned tables**

In the same migration, for each tenant-owned table from the spec, add:

```sql
ALTER TABLE <table_name> ADD COLUMN IF NOT EXISTS tenant_id uuid;
UPDATE <table_name> SET tenant_id = '00000000-0000-0000-0000-000000000001' WHERE tenant_id IS NULL;
ALTER TABLE <table_name> ALTER COLUMN tenant_id SET NOT NULL;
CREATE INDEX IF NOT EXISTS ix_<table_name>_tenant_id ON <table_name>(tenant_id);
```

Apply this to:

```text
users
roles
role_permissions
branches
halls
zones
tables
hookahs
bowls
tobaccos
inventory_items
inventory_movements
mixes
mix_items
bookings
orders
order_items
payments
notifications
notification_templates
notification_preferences
reviews
promocodes
promocode_redemptions
staff_shifts
```

- [ ] **Step 3: Add foreign keys**

For every table above, add:

```sql
ALTER TABLE <table_name>
ADD CONSTRAINT fk_<table_name>_tenant_id
FOREIGN KEY (tenant_id) REFERENCES tenants(id);
```

- [ ] **Step 4: Document migration**

Append to `docs/database.md`:

```markdown
## Tenant Foundation

Production SaaS uses a shared PostgreSQL database. Tenant-owned tables include a non-null `tenant_id` column and tenant-scoped indexes. Legacy seed data is assigned to the demo tenant `00000000-0000-0000-0000-000000000001`.
```

- [ ] **Step 5: Run migrator build**

```bash
dotnet build backend/infrastructure/migrations/src/HookahPlatform.Migrations/HookahPlatform.Migrations.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add backend/infrastructure/migrations/src/HookahPlatform.Migrations docs/database.md
git commit -m "feat: add tenant foundation migration"
```

### Task 1.5: Add Gateway Tenant Resolver

**Files:**
- Modify: `backend/services/api-gateway/src/HookahPlatform.ApiGateway/Program.cs`
- Modify: `backend/services/api-gateway/src/HookahPlatform.ApiGateway/appsettings.json`
- Modify: `docs/api.md`

- [ ] **Step 1: Add resolver rules**

In `Program.cs`, add middleware before proxy forwarding:

```csharp
app.Use(async (context, next) =>
{
    var tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(tenantId))
    {
        tenantId = context.User.FindFirst("tenant_id")?.Value;
    }

    if (string.IsNullOrWhiteSpace(tenantId))
    {
        var host = context.Request.Host.Host;
        var subdomain = host.Split('.').FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(subdomain) && !string.Equals(subdomain, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            tenantId = context.RequestServices.GetRequiredService<IConfiguration>()[$"Tenants:Subdomains:{subdomain}"];
        }
    }

    if (!string.IsNullOrWhiteSpace(tenantId))
    {
        context.Request.Headers["X-Tenant-Id"] = tenantId;
    }

    await next();
});
```

- [ ] **Step 2: Add local demo tenant mapping**

In `appsettings.json`, add:

```json
{
  "Tenants": {
    "Subdomains": {
      "demo": "00000000-0000-0000-0000-000000000001"
    }
  }
}
```

Merge with existing JSON instead of replacing the file.

- [ ] **Step 3: Document tenant header contract**

Append to `docs/api.md`:

```markdown
## Tenant Context

Tenant-owned API endpoints require `X-Tenant-Id`. The API Gateway resolves this from tenant subdomain, custom domain mapping, authenticated session claims, or an explicit trusted internal header. Domain services still validate tenant context server-side.
```

- [ ] **Step 4: Build gateway**

```bash
dotnet build backend/services/api-gateway/src/HookahPlatform.ApiGateway/HookahPlatform.ApiGateway.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add backend/services/api-gateway/src/HookahPlatform.ApiGateway/Program.cs backend/services/api-gateway/src/HookahPlatform.ApiGateway/appsettings.json docs/api.md
git commit -m "feat: resolve tenant context in gateway"
```

### Task 1.6: Tenant-Aware Events

**Files:**
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/DomainEvents.cs`
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/MessagingCatalog.cs`
- Modify: `docs/events.md`

- [ ] **Step 1: Add tenant metadata interface**

In `DomainEvents.cs`, add:

```csharp
public interface ITenantIntegrationEvent
{
    Guid TenantId { get; }
    Guid EventId { get; }
    string CorrelationId { get; }
    string? CausationId { get; }
    DateTimeOffset OccurredAt { get; }
    int SchemaVersion { get; }
}
```

- [ ] **Step 2: Update event records**

For every integration event record in `DomainEvents.cs`, add these constructor parameters:

```csharp
Guid TenantId,
Guid EventId,
string CorrelationId,
string? CausationId,
DateTimeOffset OccurredAt,
int SchemaVersion
```

Make each event implement `ITenantIntegrationEvent`.

- [ ] **Step 3: Document tenant event rule**

Append to `docs/events.md`:

```markdown
## Tenant Event Metadata

All tenant-owned integration events include `tenantId`, `eventId`, `correlationId`, `causationId`, `occurredAt` and `schemaVersion`. Consumers reject tenant-owned events without tenant metadata.
```

- [ ] **Step 4: Build contracts**

```bash
dotnet build backend/shared/contracts/src/HookahPlatform.Contracts/HookahPlatform.Contracts.csproj --no-restore
```

Expected: build succeeds after event producers are updated in later task.

- [ ] **Step 5: Commit**

```bash
git add backend/shared/contracts/src/HookahPlatform.Contracts/DomainEvents.cs backend/shared/contracts/src/HookahPlatform.Contracts/MessagingCatalog.cs docs/events.md
git commit -m "feat: add tenant metadata to integration events"
```

### Task 1.7: Tenant-Scope Service Reads And Writes

**Files:**
- Modify all service `Program.cs` files listed in "Services To Tenant-Scope".

- [ ] **Step 1: Add tenant context dependency to handlers**

For each tenant-owned endpoint handler, inject `ITenantContext tenant`.

Use this guard at the beginning of tenant-owned handlers:

```csharp
if (!tenant.TenantId.HasValue)
{
    return Results.Problem("Tenant context is required.", statusCode: StatusCodes.Status400BadRequest);
}

var tenantId = tenant.TenantId.Value;
```

- [ ] **Step 2: Filter reads by tenant**

Change tenant-owned EF queries from:

```csharp
db.Branches.Where(x => x.IsActive)
```

to:

```csharp
db.Branches.Where(x => x.TenantId == tenantId && x.IsActive)
```

Apply equivalent filters for every tenant-owned entity in each service.

- [ ] **Step 3: Set tenant_id on writes**

For new tenant-owned records, set:

```csharp
TenantId = tenantId
```

Do this before `SaveChangesAsync()`.

- [ ] **Step 4: Reject cross-tenant references**

When a write references another entity, load referenced rows with tenant filter:

```csharp
var branch = await db.Branches.FirstOrDefaultAsync(x => x.Id == request.BranchId && x.TenantId == tenantId);
if (branch is null) return Results.NotFound();
```

Apply this pattern to branch, table, hookah, bowl, mix, tobacco, booking, order, payment, review, promo, and user references.

- [ ] **Step 5: Build solution**

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add backend/services backend/shared
git commit -m "feat: enforce tenant scope in services"
```

### Task 1.8: Tenant Isolation Smoke Test

**Files:**
- Create: `scripts/tenant-isolation-smoke.mjs`
- Modify: `package.json`

- [ ] **Step 1: Create isolation smoke script**

Create `scripts/tenant-isolation-smoke.mjs`:

```javascript
const gateway = process.env.GATEWAY_URL ?? "http://localhost:8080";
const demoTenant = "00000000-0000-0000-0000-000000000001";
const otherTenant = "00000000-0000-0000-0000-000000000002";

async function request(path, tenantId) {
  const response = await fetch(`${gateway}${path}`, {
    headers: {
      "X-Tenant-Id": tenantId,
      "Content-Type": "application/json"
    }
  });
  return { status: response.status, body: await response.text() };
}

const demoBranches = await request("/api/branches", demoTenant);
if (demoBranches.status >= 400) {
  throw new Error(`Expected demo tenant branch read to succeed, got ${demoBranches.status}: ${demoBranches.body}`);
}

const otherBranches = await request("/api/branches", otherTenant);
if (otherBranches.status === 200 && otherBranches.body.includes("Hookah")) {
  throw new Error("Cross-tenant branch data leaked to other tenant");
}

console.log("tenant isolation smoke passed");
```

- [ ] **Step 2: Add package script**

In `package.json`, add:

```json
"tenant:isolation-smoke": "node scripts/tenant-isolation-smoke.mjs"
```

Keep JSON valid with commas.

- [ ] **Step 3: Run script against local gateway**

```bash
corepack pnpm tenant:isolation-smoke
```

Expected: `tenant isolation smoke passed`.

- [ ] **Step 4: Commit**

```bash
git add scripts/tenant-isolation-smoke.mjs package.json
git commit -m "test: add tenant isolation smoke"
```

---

## Phase 2 - SaaS Security And Custom RBAC

### Task 2.1: Tenant-Owned Role Schema

**Files:**
- Modify: migration project under `backend/infrastructure/migrations/src/HookahPlatform.Migrations`
- Modify: `backend/services/user-service/src/HookahPlatform.UserService/Program.cs`
- Modify: `docs/database.md`

- [ ] **Step 1: Add role tenant constraints**

Create a migration that ensures:

```sql
ALTER TABLE roles ADD COLUMN IF NOT EXISTS tenant_id uuid;
UPDATE roles SET tenant_id = '00000000-0000-0000-0000-000000000001' WHERE tenant_id IS NULL;
ALTER TABLE roles ALTER COLUMN tenant_id SET NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_tenant_code ON roles(tenant_id, code);
```

- [ ] **Step 2: Add role management endpoints**

In user service, add tenant-scoped endpoints:

```text
GET /api/roles
POST /api/roles
PATCH /api/roles/{id}
DELETE /api/roles/{id}
PUT /api/roles/{id}/permissions
```

Each endpoint uses `tenantId` from `ITenantContext`.

- [ ] **Step 3: Add branch scope table**

Add migration SQL:

```sql
CREATE TABLE IF NOT EXISTS user_branch_scopes (
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    user_id uuid NOT NULL REFERENCES users(id),
    branch_id uuid NOT NULL REFERENCES branches(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, user_id, branch_id)
);
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
git add backend/services/user-service backend/infrastructure/migrations docs/database.md
git commit -m "feat: add tenant-owned custom roles"
```

### Task 2.2: Audit Log Foundation

**Files:**
- Create: `backend/shared/building-blocks/src/HookahPlatform.BuildingBlocks/Auditing/AuditLog.cs`
- Modify: migration project
- Modify: services performing sensitive actions

- [ ] **Step 1: Add audit table migration**

Add SQL:

```sql
CREATE TABLE IF NOT EXISTS audit_logs (
    id uuid PRIMARY KEY,
    tenant_id uuid NULL REFERENCES tenants(id),
    actor_user_id uuid NULL,
    action varchar(120) NOT NULL,
    target_type varchar(120) NOT NULL,
    target_id varchar(120) NULL,
    result varchar(40) NOT NULL,
    correlation_id varchar(120) NULL,
    metadata_json jsonb NULL,
    created_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_audit_logs_tenant_created_at ON audit_logs(tenant_id, created_at DESC);
```

- [ ] **Step 2: Add audit writer interface**

Create `AuditLog.cs`:

```csharp
namespace HookahPlatform.BuildingBlocks.Auditing;

public interface IAuditLogWriter
{
    Task WriteAsync(Guid? tenantId, Guid? actorUserId, string action, string targetType, string? targetId, string result, string? correlationId, object? metadata, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Use audit writer for sensitive actions**

Add audit writes for:

```text
login/logout/refresh failure
role create/update/delete
permission assignment
payment refund
inventory adjustment
tenant settings update
provider credentials update
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
git add backend/shared backend/services backend/infrastructure/migrations
git commit -m "feat: add audit log foundation"
```

### Task 2.3: CRM Role Editor

**Files:**
- Create: `frontend/crm-app/app/admin/roles/page.tsx`
- Modify: `frontend/crm-app/lib/api.ts`
- Modify: `frontend/crm-app/lib/store.ts`

- [ ] **Step 1: Add role API client functions**

Add functions in `frontend/crm-app/lib/api.ts`:

```typescript
export type TenantRole = {
  id: string;
  tenantId: string;
  name: string;
  code: string;
  permissions: string[];
  isActive: boolean;
};

export function getRoles(token?: string) {
  return getJson<TenantRole[]>("/api/roles", token);
}

export function createRole(input: { name: string; code: string; permissions: string[] }, token?: string) {
  return postJson<TenantRole>("/api/roles", input, token);
}

export function updateRole(id: string, input: { name: string; permissions: string[]; isActive: boolean }, token?: string) {
  return patchJson<TenantRole>(`/api/roles/${id}`, input, token);
}
```

- [ ] **Step 2: Create role editor page**

Create `frontend/crm-app/app/admin/roles/page.tsx` with a client component that:

```text
loads /api/roles
lists roles
creates role with name/code
edits permissions as checkboxes from permission catalog
deactivates role
shows loading/error/empty states
```

- [ ] **Step 3: Build frontend and commit**

```bash
corepack pnpm --dir frontend/crm-app build
git add frontend/crm-app
git commit -m "feat: add CRM custom role editor"
```

---

## Phase 3 - Tenant Payments And Notifications

### Task 3.1: Tenant Payment Credentials

**Files:**
- Modify: `backend/services/payment-service/src/HookahPlatform.PaymentService/Program.cs`
- Modify: `backend/services/payment-service/src/HookahPlatform.PaymentService/Persistence/PaymentDbContext.cs`
- Modify: migration project
- Modify: `docs/api.md`

- [ ] **Step 1: Add provider credentials table**

Add migration SQL:

```sql
CREATE TABLE IF NOT EXISTS tenant_payment_providers (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    provider varchar(80) NOT NULL,
    display_name varchar(160) NOT NULL,
    encrypted_credentials text NOT NULL,
    webhook_secret_hash text NOT NULL,
    is_active boolean NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_payment_provider ON tenant_payment_providers(tenant_id, provider, display_name);
```

- [ ] **Step 2: Route payment creation by tenant**

Payment creation must:

```text
read tenant context
load active provider credentials for tenant
reject if provider is not configured
store tenant_id on payment
include tenant metadata in provider request
```

- [ ] **Step 3: Validate webhook tenant**

Webhook handler must:

```text
resolve provider account or signed metadata to tenant
validate webhook signature/secret
load payment by tenant_id and external_payment_id
reject cross-tenant mutation
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build backend/services/payment-service/src/HookahPlatform.PaymentService/HookahPlatform.PaymentService.csproj --no-restore
git add backend/services/payment-service backend/infrastructure/migrations docs/api.md
git commit -m "feat: add tenant-owned payment providers"
```

### Task 3.2: Tenant Notification Channels

**Files:**
- Modify: `backend/services/notification-service/src/HookahPlatform.NotificationService/Program.cs`
- Modify: migration project
- Modify: `docs/api.md`

- [ ] **Step 1: Add tenant notification channel table**

Add SQL:

```sql
CREATE TABLE IF NOT EXISTS tenant_notification_channels (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    channel varchar(40) NOT NULL,
    encrypted_settings text NOT NULL,
    is_active boolean NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_notification_channel ON tenant_notification_channels(tenant_id, channel);
```

- [ ] **Step 2: Make templates tenant-owned**

Ensure notification templates and preferences query by `tenant_id`.

- [ ] **Step 3: Add delivery audit**

Add SQL:

```sql
CREATE TABLE IF NOT EXISTS notification_deliveries (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(id),
    notification_id uuid NULL,
    channel varchar(40) NOT NULL,
    recipient varchar(255) NOT NULL,
    status varchar(40) NOT NULL,
    provider_message_id varchar(255) NULL,
    error text NULL,
    created_at timestamptz NOT NULL
);
```

- [ ] **Step 4: Build and commit**

```bash
dotnet build backend/services/notification-service/src/HookahPlatform.NotificationService/HookahPlatform.NotificationService.csproj --no-restore
git add backend/services/notification-service backend/infrastructure/migrations docs/api.md
git commit -m "feat: add tenant notification channels"
```

---

## Phase 4 - Production Frontend

### Task 4.1: Tenant Admin Console

**Files:**
- Create: `frontend/crm-app/app/admin/tenants/page.tsx`
- Modify: `frontend/crm-app/lib/api.ts`
- Modify: `frontend/crm-app/app/page.tsx`
- Modify: `docs/frontend.md`

- [ ] **Step 1: Add tenant API types**

Add to `frontend/crm-app/lib/api.ts`:

```typescript
export type Tenant = {
  id: string;
  name: string;
  slug: string;
  status: string;
  primaryDomain?: string | null;
  createdAt: string;
  updatedAt: string;
};

export function getTenants(token?: string) {
  return getJson<Tenant[]>("/api/tenants", token);
}
```

- [ ] **Step 2: Create admin tenant page**

Create `frontend/crm-app/app/admin/tenants/page.tsx` with this minimum implementation shape:

```typescript
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { createTenant, getTenants, updateTenant } from "../../lib/api";
import { useCrmStore } from "../../lib/store";
import { ActionButton, CrudToolbar, EmptyState, FormError, FormField, LoadingState } from "../../lib/ui";

export default function TenantAdminPage() {
  const queryClient = useQueryClient();
  const token = useCrmStore((state) => state.session.accessToken);
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [timezone, setTimezone] = useState("Europe/Moscow");
  const [currency, setCurrency] = useState("RUB");
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: () => getTenants(token), enabled: Boolean(token) });
  const create = useMutation({
    mutationFn: () => createTenant({ name: name.trim(), slug: slug.trim(), timezone, currency }, token),
    onSuccess: async () => {
      setName("");
      setSlug("");
      await queryClient.invalidateQueries({ queryKey: ["tenants"] });
    }
  });

  if (tenants.isLoading) return <LoadingState label="Loading tenants..." />;
  if (tenants.isError) return <FormError error={tenants.error} message="Failed to load tenants" />;

  return <main className="section">
    <div className="section-head"><h1>Tenants</h1><span className="meta">Platform admin</span></div>
    <CrudToolbar title="New tenant">
      <FormField label="Name"><input value={name} onChange={(event) => setName(event.target.value)} /></FormField>
      <FormField label="Slug"><input value={slug} onChange={(event) => setSlug(event.target.value.toLowerCase())} /></FormField>
      <FormField label="Timezone"><input value={timezone} onChange={(event) => setTimezone(event.target.value)} /></FormField>
      <FormField label="Currency"><input value={currency} onChange={(event) => setCurrency(event.target.value.toUpperCase())} /></FormField>
      <ActionButton disabled={!name.trim() || !slug.trim()} pending={create.isPending} onClick={() => create.mutate()}>Create</ActionButton>
    </CrudToolbar>
    {tenants.data?.length ? tenants.data.map((tenant) => <TenantRow tenant={tenant} token={token} key={tenant.id} />) : <EmptyState label="No tenants" />}
  </main>;
}

function TenantRow({ tenant, token }: { tenant: { id: string; name: string; slug: string; status: string; primaryDomain?: string | null }, token?: string }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(tenant.name);
  const [primaryDomain, setPrimaryDomain] = useState(tenant.primaryDomain ?? "");
  const [status, setStatus] = useState(tenant.status);
  const update = useMutation({
    mutationFn: () => updateTenant(tenant.id, { name, primaryDomain: primaryDomain || null, status }, token),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["tenants"] })
  });
  return <article className="booking-row">
    <strong>{tenant.slug}</strong>
    <FormField label="Name"><input value={name} onChange={(event) => setName(event.target.value)} /></FormField>
    <FormField label="Domain"><input value={primaryDomain} onChange={(event) => setPrimaryDomain(event.target.value)} /></FormField>
    <FormField label="Status"><select value={status} onChange={(event) => setStatus(event.target.value)}><option>ACTIVE</option><option>SUSPENDED</option></select></FormField>
    <ActionButton pending={update.isPending} disabled={!name.trim()} onClick={() => update.mutate()}>Save</ActionButton>
  </article>;
}
```

Also add `createTenant` and `updateTenant` API helpers in `frontend/crm-app/lib/api.ts`.

- [ ] **Step 3: Build and commit**

```bash
corepack pnpm --dir frontend/crm-app build
git add frontend/crm-app docs/frontend.md
git commit -m "feat: add tenant admin console"
```

### Task 4.2: Tenant-Aware Client Branding

**Files:**
- Modify: `frontend/client-app/lib/api.ts`
- Modify: `frontend/client-app/lib/store.ts`
- Modify: `frontend/client-app/app/page.tsx`
- Modify: `frontend/client-app/app/globals.css`

- [ ] **Step 1: Add tenant branding type**

Add:

```typescript
export type TenantBranding = {
  tenantId: string;
  name: string;
  logoUrl?: string | null;
  primaryColor: string;
  accentColor: string;
};
```

- [ ] **Step 2: Load branding on client home**

Client app must call:

```text
GET /api/public/tenant/branding
```

and apply CSS variables:

```css
--tenant-primary
--tenant-accent
```

- [ ] **Step 3: Build and commit**

```bash
corepack pnpm --dir frontend/client-app build
git add frontend/client-app
git commit -m "feat: add tenant branding to client app"
```

---

## Phase 5 - Kubernetes, CI/CD, And Operations

### Task 5.1: Helm Chart Skeleton

**Files:**
- Create: `infrastructure/helm/hookah-platform/Chart.yaml`
- Create: `infrastructure/helm/hookah-platform/values.yaml`
- Create: `infrastructure/helm/hookah-platform/templates/deployment.yaml`
- Create: `infrastructure/helm/hookah-platform/templates/service.yaml`
- Create: `infrastructure/helm/hookah-platform/templates/ingress.yaml`
- Create: `infrastructure/helm/hookah-platform/templates/migrations-job.yaml`

- [ ] **Step 1: Create chart metadata**

Create `Chart.yaml`:

```yaml
apiVersion: v2
name: hookah-platform
description: Hookah CRM Platform SaaS deployment
type: application
version: 0.1.0
appVersion: "0.1.0"
```

- [ ] **Step 2: Create values**

Create `values.yaml`:

```yaml
imageRegistry: ghcr.io/hookah-platform/hookah-platform
imagePullPolicy: IfNotPresent

ingress:
  enabled: true
  className: nginx
  host: hookah.local
  tlsSecretName: hookah-platform-tls

services:
  apiGateway:
    image: api-gateway
    replicas: 2
    port: 8080
  tenantService:
    image: tenant-service
    replicas: 2
    port: 8080

postgres:
  connectionSecretName: hookah-postgres

redis:
  connectionSecretName: hookah-redis

rabbitmq:
  connectionSecretName: hookah-rabbitmq
```

- [ ] **Step 3: Add Kubernetes templates**

Create `templates/deployment.yaml`:

```yaml
{{- range $name, $service := .Values.services }}
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "hookah-platform.fullname" $ }}-{{ $name }}
spec:
  replicas: {{ $service.replicas }}
  selector:
    matchLabels:
      app.kubernetes.io/name: {{ include "hookah-platform.name" $ }}
      app.kubernetes.io/component: {{ $name }}
  template:
    metadata:
      labels:
        app.kubernetes.io/name: {{ include "hookah-platform.name" $ }}
        app.kubernetes.io/component: {{ $name }}
    spec:
      containers:
        - name: {{ $name }}
          image: "{{ $.Values.imageRegistry }}/{{ $service.image }}:{{ $.Chart.AppVersion }}"
          imagePullPolicy: {{ $.Values.imagePullPolicy }}
          ports:
            - containerPort: {{ $service.port }}
          readinessProbe:
            httpGet:
              path: /health
              port: {{ $service.port }}
          livenessProbe:
            httpGet:
              path: /health
              port: {{ $service.port }}
---
{{- end }}
```

Create `templates/service.yaml`:

```yaml
{{- range $name, $service := .Values.services }}
apiVersion: v1
kind: Service
metadata:
  name: {{ include "hookah-platform.fullname" $ }}-{{ $name }}
spec:
  selector:
    app.kubernetes.io/name: {{ include "hookah-platform.name" $ }}
    app.kubernetes.io/component: {{ $name }}
  ports:
    - port: {{ $service.port }}
      targetPort: {{ $service.port }}
---
{{- end }}
```

Create `templates/ingress.yaml`:

```yaml
{{- if .Values.ingress.enabled }}
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: {{ include "hookah-platform.fullname" . }}
spec:
  ingressClassName: {{ .Values.ingress.className }}
  tls:
    - hosts: [{{ .Values.ingress.host | quote }}]
      secretName: {{ .Values.ingress.tlsSecretName }}
  rules:
    - host: {{ .Values.ingress.host | quote }}
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: {{ include "hookah-platform.fullname" . }}-apiGateway
                port:
                  number: {{ .Values.services.apiGateway.port }}
{{- end }}
```

Create `templates/migrations-job.yaml`:

```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: {{ include "hookah-platform.fullname" . }}-migrations
spec:
  template:
    spec:
      restartPolicy: OnFailure
      containers:
        - name: migrations
          image: "{{ .Values.imageRegistry }}/migrations:{{ .Chart.AppVersion }}"
          imagePullPolicy: {{ .Values.imagePullPolicy }}
          envFrom:
            - secretRef:
                name: {{ .Values.postgres.connectionSecretName }}
```

Create `templates/_helpers.tpl`:

```yaml
{{- define "hookah-platform.name" -}}
{{ .Chart.Name }}
{{- end }}

{{- define "hookah-platform.fullname" -}}
{{ .Release.Name }}-{{ .Chart.Name }}
{{- end }}
```

- [ ] **Step 4: Lint chart**

```bash
helm lint infrastructure/helm/hookah-platform
```

Expected: `1 chart(s) linted, 0 chart(s) failed`.

- [ ] **Step 5: Commit**

```bash
git add infrastructure/helm
git commit -m "infra: add Helm chart skeleton"
```

### Task 5.2: CI/CD Workflows

**Files:**
- Modify: `.github/workflows/backend.yml`
- Modify: `.github/workflows/frontend.yml`
- Create: `.github/workflows/container-images.yml`
- Create: `.github/workflows/deploy-staging.yml`

- [ ] **Step 1: Backend workflow gates**

Ensure backend workflow runs:

```bash
dotnet restore backend/HookahPlatform.sln
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
dotnet test backend/HookahPlatform.sln --no-build
```

- [ ] **Step 2: Frontend workflow gates**

Ensure frontend workflow runs:

```bash
corepack enable
corepack pnpm install --frozen-lockfile
corepack pnpm frontend:build
corepack pnpm frontend:smoke
```

- [ ] **Step 3: Container workflow**

Create `.github/workflows/container-images.yml`:

```yaml
name: container-images

on:
  push:
    branches: [main]
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    strategy:
      matrix:
        image:
          - api-gateway
          - auth-service
          - user-service
          - tenant-service
          - branch-service
          - mixology-service
          - inventory-service
          - order-service
          - booking-service
          - payment-service
          - notification-service
          - analytics-service
          - review-service
          - promo-service
          - crm-app
          - client-app
    steps:
      - uses: actions/checkout@v4
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - uses: docker/build-push-action@v6
        with:
          context: .
          file: ${{ contains(matrix.image, 'app') && 'frontend/Dockerfile' || 'backend/Dockerfile' }}
          push: true
          tags: ghcr.io/${{ github.repository }}/${{ matrix.image }}:${{ github.sha }}
```

- [ ] **Step 4: Staging deploy workflow**

Create `.github/workflows/deploy-staging.yml`:

```yaml
name: deploy-staging

on:
  workflow_dispatch:

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: staging
    steps:
      - uses: actions/checkout@v4
      - uses: azure/setup-helm@v4
      - name: Lint chart
        run: helm lint infrastructure/helm/hookah-platform
      - name: Configure kubeconfig
        run: |
          mkdir -p ~/.kube
          echo "${{ secrets.STAGING_KUBECONFIG }}" > ~/.kube/config
          chmod 600 ~/.kube/config
      - name: Deploy
        run: |
          helm upgrade --install hookah-platform infrastructure/helm/hookah-platform \
            --namespace hookah-staging \
            --create-namespace \
            --set imageRegistry=ghcr.io/${{ github.repository }} \
            --set-string appVersion=${{ github.sha }}
      - name: Run tenant isolation smoke
        env:
          GATEWAY_URL: ${{ secrets.STAGING_GATEWAY_URL }}
        run: node scripts/tenant-isolation-smoke.mjs
```

- [ ] **Step 5: Commit**

```bash
git add .github/workflows
git commit -m "ci: add production pipeline gates"
```

---

## Phase 6 - SaaS Hardening

### Task 6.1: Tenant Lifecycle

**Files:**
- Modify: `backend/services/tenant-service/src/HookahPlatform.TenantService/Program.cs`
- Modify: `docs/tenant-model.md`

- [ ] **Step 1: Document tenant lifecycle states**

Create or update `docs/tenant-model.md`:

```markdown
# Tenant Model

## States

- `ACTIVE`: tenant can operate normally.
- `SUSPENDED`: users can sign in only if platform-admin; business writes are rejected.
- `DELETING`: tenant is locked while export/deletion runs.
- `DELETED`: tenant cannot be used; retained records are anonymized or removed according to retention policy.
```

- [ ] **Step 2: Add lifecycle endpoints**

Tenant service endpoints:

```text
PATCH /api/tenants/{id}/suspend
PATCH /api/tenants/{id}/reactivate
POST /api/tenants/{id}/export
DELETE /api/tenants/{id}
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build backend/services/tenant-service/src/HookahPlatform.TenantService/HookahPlatform.TenantService.csproj --no-restore
git add backend/services/tenant-service docs/tenant-model.md
git commit -m "feat: add tenant lifecycle endpoints"
```

### Task 6.2: Backup Restore Drill Documentation

**Files:**
- Create: `docs/backup-restore.md`

- [ ] **Step 1: Create backup restore doc**

Create:

```markdown
# Backup And Restore

## Backup Requirements

- PostgreSQL backups are encrypted.
- Redis runtime data is treated as disposable unless explicitly persisted by service logic.
- RabbitMQ messages are recoverable through the outbox table.

## Restore Drill

1. Restore PostgreSQL backup into staging.
2. Run migration status check.
3. Start services with restored database.
4. Run tenant isolation smoke.
5. Run payment webhook replay test with test provider payload.
6. Record drill result and restore timestamp.
```

- [ ] **Step 2: Commit**

```bash
git add docs/backup-restore.md
git commit -m "docs: add backup restore drill"
```

---

## Final Verification

- [ ] **Step 1: Run backend build**

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
```

Expected: build succeeds.

- [ ] **Step 2: Run frontend build**

```bash
corepack pnpm frontend:build
```

Expected: CRM and client builds succeed.

- [ ] **Step 3: Run smoke scripts against running local stack**

```bash
corepack pnpm api:crud-smoke
corepack pnpm frontend:smoke
corepack pnpm frontend:browser-smoke
corepack pnpm tenant:isolation-smoke
```

Expected: all scripts exit with code `0`.

- [ ] **Step 4: Verify Kubernetes manifests**

```bash
helm lint infrastructure/helm/hookah-platform
```

Expected: chart lint succeeds.

- [ ] **Step 5: Final documentation commit**

```bash
git add docs README.md
git commit -m "docs: finalize SaaS production readiness"
```
