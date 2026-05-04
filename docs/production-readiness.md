# Production Readiness

## Prerequisites

- Backend restore: `dotnet restore backend/HookahPlatform.sln -m:1 /nr:false`
- Frontend install: `corepack pnpm install --frozen-lockfile`

## Baseline Commands

- Backend build: `dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false`
- Frontend build: `corepack pnpm frontend:build`
- Local stack: `corepack pnpm local:up`

## Smoke Flow

1. Start local stack: `corepack pnpm local:up`
2. API smoke (requires a running gateway): `corepack pnpm api:crud-smoke`
3. Frontend HTTP smoke (requires running CRM and client apps): `corepack pnpm frontend:smoke`
4. Browser smoke (requires running CRM and client apps): `corepack pnpm frontend:browser-smoke`

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
