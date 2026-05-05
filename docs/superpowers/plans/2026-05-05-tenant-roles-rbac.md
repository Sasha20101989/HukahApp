# Tenant Roles + RBAC (DB-Backed) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make permissions production-grade by moving role/permission management to PostgreSQL (tenant-scoped), keeping `OWNER` as a non-deletable system role, and enforcing authorization in the API Gateway via DB-backed permissions with Redis cache.

**Architecture:** User-service becomes the source of truth for tenant roles and their permissions. API Gateway resolves permissions per request (or via short Redis cache) and forwards `X-User-*` + `X-Tenant-*` to services. Services keep validating forwarded headers but no longer depend on `RolePermissionCatalog` for live authorization decisions.

**Tech Stack:** ASP.NET Core 8, EF Core/Npgsql, PostgreSQL, Redis (`IDistributedCache`), Next.js smoke checks, Docker Compose.

---

## File Map

**Migrations**
- Modify: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/Sql/001_init.sql`
- Create: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/Migrations/*_RoleRbacFoundation.cs` (+ `.Designer.cs`)

**Contracts (shared)**
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/DomainCatalog.cs`
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/EndpointAccessPolicy.cs`

**User Service**
- Modify: `backend/services/user-service/src/HookahPlatform.UserService/Persistence/UserDbContext.cs`
- Modify: `backend/services/user-service/src/HookahPlatform.UserService/Program.cs`

**API Gateway**
- Modify: `backend/services/api-gateway/src/HookahPlatform.ApiGateway/Program.cs`

**Smoke**
- Modify: `scripts/api-crud-smoke.mjs` (extend coverage for role CRUD)

---

## Task 1: RBAC DB Foundation (Migrations + Seeds)

**Files:**
- Modify: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/Sql/001_init.sql`
- Create: `backend/infrastructure/migrations/src/HookahPlatform.Migrations/Migrations/*_AddRoleRbacFoundation.cs`

- [ ] **Step 1: Add missing `tenants.manage` permission seed to baseline SQL**

In `backend/infrastructure/migrations/src/HookahPlatform.Migrations/Sql/001_init.sql`, extend `insert into permissions` to include:

```sql
('02000000-0000-0000-0000-000000000009', 'tenants.manage', 'Manage SaaS tenants and tenant settings')
```

Also extend `MANAGER` role mapping if desired (optional in this task) or keep it for later policy changes.

- [ ] **Step 2: Add roles RBAC columns**

Create a new EF migration (timestamp will differ) that applies:

1. `roles.tenant_id` already exists (added earlier), keep it.
2. Add:
   - `roles.is_system boolean not null default false`
   - `roles.is_active boolean not null default true`
3. Ensure demo tenant `OWNER` is system:
   - `update roles set is_system=true where tenant_id='111...111' and code='OWNER';`
4. Create helper indexes:
   - `ix_roles_tenant_id` (already created earlier but keep idempotent)
   - `ix_roles_tenant_is_active` on `(tenant_id, is_active)`

If the repo standard is “SQL-in-migration”, prefer `migrationBuilder.Sql(""" ... """);` with `if not exists` checks.

- [ ] **Step 3: Apply migrations locally**

Run:

```bash
docker compose -p hookah-platform -f infrastructure/docker-compose.yml run --rm db-migrator
```

Expected: `Database migrations applied.`

- [ ] **Step 4: Verify schema quickly**

Run:

```bash
corepack pnpm api:crud-smoke
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/infrastructure/migrations/src/HookahPlatform.Migrations/Sql/001_init.sql backend/infrastructure/migrations/src/HookahPlatform.Migrations/Migrations
git commit -m "db: add tenant role rbac foundation"
```

---

## Task 2: Replace In-Memory Role Catalog With DB Roles In User-Service

**Files:**
- Modify: `backend/services/user-service/src/HookahPlatform.UserService/Persistence/UserDbContext.cs`
- Modify: `backend/services/user-service/src/HookahPlatform.UserService/Program.cs`
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/DomainCatalog.cs`

- [ ] **Step 1: Extend EF models**

In `UserDbContext.cs`, update role entities:

```csharp
public sealed class RoleEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
}
```

Map columns:

```csharp
modelBuilder.Entity<RoleEntity>(entity =>
{
    entity.ToTable("roles");
    entity.HasKey(role => role.Id);
    entity.Property(role => role.TenantId).HasColumnName("tenant_id");
    entity.Property(role => role.IsSystem).HasColumnName("is_system");
    entity.Property(role => role.IsActive).HasColumnName("is_active");
});
```

Also map `permissions` and `role_permissions` as needed (keep existing mapping, but add column names if missing).

- [ ] **Step 2: Add DTOs (in Program.cs locally for now)**

In `Program.cs` add records:

```csharp
public sealed record RoleDto(Guid Id, string Name, string Code, bool IsSystem, bool IsActive, IReadOnlyCollection<string> Permissions);
public sealed record CreateRoleRequest(string Name, string Code);
public sealed record UpdateRoleRequest(string? Name, bool? IsActive);
public sealed record UpdateRolePermissionsRequest(IReadOnlyCollection<string> Permissions);
```

- [ ] **Step 3: Implement role list endpoints (DB-backed, tenant-scoped)**

Replace current `/api/roles` handler with:
1. Resolve `tenantId = tenantContext.GetTenantIdOrDemo()`
2. Load roles: `db.Roles.Where(r => r.TenantId == tenantId)`
3. Load permission codes for each role via `role_permissions` join `permissions`
4. Return `RoleDto[]`.

- [ ] **Step 4: Implement role CRUD**

Add:
- `POST /api/roles`
  - Validate `Name` non-empty.
  - Normalize `Code` (`Trim().ToUpperInvariant()`), forbid empty, forbid non `[A-Z0-9_]+` (simple regex).
  - Ensure unique `(tenant_id, code)`; on conflict return 409.
  - `IsSystem=false`, `IsActive=true`, `TenantId=tenantId`.
- `PATCH /api/roles/{id:guid}`
  - Tenant-scoped lookup.
  - Reject changes to system role fields other than name (optional) but:
    - forbid setting `IsActive=false` when `IsSystem=true`.
- `DELETE /api/roles/{id:guid}`
  - Tenant-scoped lookup.
  - If `IsSystem` -> 409.
  - If any users reference roleId -> 409.
  - Else delete role (cascade removes role_permissions by FK).
- `PUT /api/roles/{id:guid}/permissions`
  - Tenant-scoped role lookup.
  - If system OWNER: force `["*"]` (ignore requested) OR accept only if contains `"*"` (pick one and document).
  - Validate requested permissions exist in `permissions` table OR `"*"` special-case.
  - Replace existing role_permissions rows.

- [ ] **Step 5: Make `/api/permissions` DB-backed**

Replace catalog return with:
`db.Permissions.OrderBy(p => p.Code)`

- [ ] **Step 6: Make `/api/users/{id}/permissions` DB-backed**

Return:
- `roleCode` from user.role_id join roles
- permission codes via joins (or `["*"]` for owner if configured)

- [ ] **Step 7: Build**

```bash
dotnet build backend/services/user-service/src/HookahPlatform.UserService/HookahPlatform.UserService.csproj --no-restore -m:1 /nr:false
```

Expected: `Build succeeded`.

- [ ] **Step 8: Commit**

```bash
git add backend/services/user-service/src/HookahPlatform.UserService
git commit -m "feat: tenant-scoped role crud in user service"
```

---

## Task 3: Gateway Authorization Uses DB Permissions (With Redis Cache)

**Files:**
- Modify: `backend/services/api-gateway/src/HookahPlatform.ApiGateway/Program.cs`
- Modify: `backend/shared/contracts/src/HookahPlatform.Contracts/EndpointAccessPolicy.cs`

- [ ] **Step 1: Add endpoint access rules for tenant admin**

In `EndpointAccessPolicy.cs`, add rules:
- `GET /api/tenants` -> `tenants.manage`
- `POST /api/tenants` -> `tenants.manage`
- `PATCH /api/tenants` -> `tenants.manage`
- `GET /api/tenants/` -> `tenants.manage`
- `PUT /api/tenants/` -> `tenants.manage`

Also update any existing rules that should require `staff.manage` for role CRUD:
- `GET /api/roles` (already)
- `POST /api/roles` -> `staff.manage`
- `PATCH /api/roles` -> `staff.manage`
- `DELETE /api/roles` -> `staff.manage`
- `PUT /api/roles/*/permissions` -> `staff.manage`

- [ ] **Step 2: Add distributed cache to gateway**

In gateway `Program.cs`, register Redis `IDistributedCache` when enabled (follow existing building-blocks patterns). Add config key:
- `Redis:Enabled=true` in docker compose for gateway (if not already).

- [ ] **Step 3: Implement permission resolution**

Replace:
```csharp
var permissions = RolePermissionCatalog.GetPermissions(principal.Role);
```

With:
1. If no principal -> none.
2. Determine tenantId that gateway forwards.
3. Cache key: `t:{tenantId}:authz:user:{userId}:perms` (ttl 60s)
4. On cache miss, call user-service:
   - `GET /api/users/{userId}/permissions` (or `/api/users/me/permissions` using forwarded user headers)
   - Include `X-Tenant-Id`
   - Add `X-Gateway-Secret` so user-service can trust it as service call (match existing service secret scheme).
5. Store list in cache.
6. Forward `X-User-Permissions` as csv.

Failure behavior:
- If user-service call fails: return 503 with `code=authz_unavailable` (do not “fail open”).

- [ ] **Step 4: Build**

```bash
dotnet build backend/services/api-gateway/src/HookahPlatform.ApiGateway/HookahPlatform.ApiGateway.csproj --no-restore -m:1 /nr:false
```

- [ ] **Step 5: Commit**

```bash
git add backend/services/api-gateway/src/HookahPlatform.ApiGateway/Program.cs backend/shared/contracts/src/HookahPlatform.Contracts/EndpointAccessPolicy.cs
git commit -m "feat: gateway resolves db-backed permissions with redis cache"
```

---

## Task 4: Update Smoke Coverage For Role CRUD

**Files:**
- Modify: `scripts/api-crud-smoke.mjs`

- [ ] **Step 1: Extend smoke to create/update/delete a custom role**

Add a step after staff CRUD:
1. `POST /api/roles` with `{ name: "...", code: "SMOKE_CUSTOM_<suffix>" }`
2. `PUT /api/roles/{id}/permissions` with a small set (`orders.manage`, `bookings.manage`)
3. `PATCH /api/roles/{id}` set name and deactivate `isActive=false`
4. `DELETE /api/roles/{id}` should succeed only if no users are using it

Also add a negative case:
- attempt to `DELETE` owner system role -> expect 409.

- [ ] **Step 2: Run smoke**

```bash
corepack pnpm api:crud-smoke
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add scripts/api-crud-smoke.mjs
git commit -m "test: cover tenant role crud in api smoke"
```

---

## Task 5: Full Gate Run

- [ ] **Step 1: Local up**

```bash
corepack pnpm local:up
```

- [ ] **Step 2: Gates**

```bash
dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false
corepack pnpm frontend:build
corepack pnpm api:crud-smoke
corepack pnpm tenant:isolation-smoke
corepack pnpm frontend:smoke
corepack pnpm frontend:browser-smoke
```

Expected: all PASS.

- [ ] **Step 3: Final commit (if any remaining edits)**

```bash
git status --porcelain
```

Expected: clean.

