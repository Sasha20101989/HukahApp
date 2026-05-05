# Production Readiness

## Prerequisites

- Backend restore: `dotnet restore backend/HookahPlatform.sln -m:1 /nr:false`
- Frontend install: `corepack pnpm install --frozen-lockfile`

## Baseline Commands

- Backend build: `dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false`
- Frontend build: `corepack pnpm frontend:build`
- Local stack: `corepack pnpm local:up`
- Local stack (no Docker cache): `corepack pnpm local:up:nocache`
- Stop stack (keep volumes): `corepack pnpm local:down`
- Stop stack (drop volumes): `corepack pnpm local:down:volumes`

## Smoke Flow

1. Start local stack: `corepack pnpm local:up`
2. API smoke (requires a running gateway): `corepack pnpm api:crud-smoke`
3. Tenant isolation smoke (requires a running gateway): `corepack pnpm tenant:isolation-smoke`
4. Frontend HTTP smoke (requires running CRM and client apps): `corepack pnpm frontend:smoke`
5. Browser smoke (requires running CRM and client apps): `corepack pnpm frontend:browser-smoke`

## Production Gates

- No `package-lock.json` in the pnpm workspace.
- Docker Compose starts the full stack locally.
- Backend build passes.
- Frontend build passes.
- API smoke passes against a running gateway.
- Tenant isolation smoke passes against a running gateway.
- Frontend smoke passes against running CRM and client apps.
- Browser smoke passes against running CRM and client apps.

## Current Known Pre-SaaS Gaps

- Tenant model is not first-class.
- Tenant isolation tests do not exist.
- Kubernetes manifests are not production complete.
- CI/CD does not yet publish and deploy all service images.

## Redis Usage Map

Redis is used via `IDistributedCache` (StackExchange.Redis when `Redis:Enabled=true`, otherwise in-memory).

- Auth refresh sessions:
  - Key: `t:{tenantId}:auth:refresh:{tokenHash}`
  - TTL: refresh token TTL (typically 30 days)
  - Source: `auth-service`
- Booking availability cache:
  - Key: `t:{tenantId}:booking:availability:{branchId}:{date}:{time}:{guestsCount}`
  - TTL: 10 seconds
  - Source: `booking-service`
- Booking holds (temporary time locks during client checkout):
  - Keys: `t:{tenantId}:booking:hold:*` (see `booking-service` for exact key formats)
  - TTL: 10 minutes (hold expires automatically)
  - Source: `booking-service`
- Coal timer state:
  - Key: `t:{tenantId}:order:{orderId}:coal-timer`
  - TTL: 4 hours
  - Source: `order-service`
- CRM fast runtime state (active orders per branch):
  - Keys:
    - `t:{tenantId}:crm:branch:{branchId}:active-orders` (index)
    - `t:{tenantId}:crm:order:{orderId}:state` (per-order state)
  - TTL: 12 hours
  - Source: `order-service`
