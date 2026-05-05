# Hookah CRM Platform

Executable monorepo foundation for a hookah CRM/ERP platform.

## Structure

- `backend/HookahPlatform.sln` - .NET solution.
- `backend/services` - API Gateway and microservice hosts.
- `backend/shared/contracts` - integration events and public contracts.
- `backend/shared/building-blocks` - shared API defaults and domain rules.
- `infrastructure/docker-compose.yml` - PostgreSQL, Redis, RabbitMQ, OpenTelemetry Collector and service containers.
- `docs` - architecture, API, events, observability and database notes.

## Build

```bash
rtk env DOTNET_CLI_HOME=/tmp dotnet build backend/HookahPlatform.sln
```

## Run Full Platform Locally

```bash
corepack pnpm local:up
```

This starts PostgreSQL, Redis, RabbitMQ, OpenTelemetry Collector, EF migrator, all backend services, API Gateway, CRM app, client app and Nginx through Docker Compose.

Useful variants:

```bash
corepack pnpm local:up:logs
corepack pnpm local:up:nocache
corepack pnpm local:reset
corepack pnpm local:down
corepack pnpm local:down:volumes
```

Public URLs:

- Client app: `http://localhost:3001`
- CRM app: `http://localhost:3000`
- API Gateway: `http://localhost:8080`
- Nginx client: `http://localhost`
- Nginx CRM: `http://localhost/crm/`
- RabbitMQ UI: `http://localhost:15672` (`guest` / `guest`)
- OpenTelemetry OTLP gRPC: `http://localhost:4317`
- OpenTelemetry OTLP HTTP: `http://localhost:4318`
- Prometheus metrics from collector: `http://localhost:9464/metrics`

## Run One Service

```bash
rtk env DOTNET_CLI_HOME=/tmp dotnet run --project backend/services/mixology-service/src/HookahPlatform.MixologyService/HookahPlatform.MixologyService.csproj
```

## Notes

The local machine currently has .NET SDK 8.0, so projects target `net8.0`. The requested ASP.NET Core 9 upgrade should be done after installing SDK 9.

The API Gateway is the public entrypoint at `http://localhost:8080`. Service containers communicate on the internal Docker network.

## API Smoke Tests

Use [docs/smoke-tests.http](docs/smoke-tests.http) after starting Docker Compose. Requests are written against the gateway.

For automated CRUD coverage against a running local gateway:

```bash
corepack pnpm api:crud-smoke
```

The script logs in as the seeded owner by default, registers an isolated smoke client when `SMOKE_CLIENT_ID` is not provided, creates isolated `SMOKE-*` resources, verifies create/update/delete or deactivate flows, and then cleans up. Override `API_URL`, `SMOKE_OWNER_PHONE`, `SMOKE_OWNER_PASSWORD`, `SMOKE_CLIENT_ID` or `SMOKE_TIMEOUT_MS` when needed.

## Frontend Smoke Tests

After `corepack pnpm local:up` or after starting both frontend dev servers:

```bash
corepack pnpm frontend:smoke
corepack pnpm frontend:browser-smoke
```

`frontend:smoke` performs HTTP checks for CRM/client pages, route guards, manifests and payment return pages. `frontend:browser-smoke` renders the same critical paths in headless Chrome/Chromium. Set `CRM_URL`, `CLIENT_URL`, `CHROME_BIN` or `SMOKE_TIMEOUT_MS` when ports or browser path differ.

## Observability

Backend services emit structured Serilog logs plus OpenTelemetry traces/metrics through the local collector. See [docs/observability.md](docs/observability.md).

## Frontend

```bash
corepack pnpm install
corepack pnpm crm:dev
corepack pnpm client:dev
```

CRM runs on `http://localhost:3000`. Client app runs on `http://localhost:3001`.

Docker Compose also includes `crm-app` and `client-app` services.

With Nginx enabled in Compose, `http://localhost` serves the client app, `http://localhost/crm/` serves CRM, and `http://localhost/api/` proxies API Gateway.

## Kubernetes

Base manifests live in `infrastructure/kubernetes`.

## Frontend Product Flows

See [docs/frontend.md](docs/frontend.md) for the CRM tablet workspace and client booking PWA flows.
