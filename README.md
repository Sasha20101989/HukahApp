# Hookah CRM Platform

Executable monorepo foundation for a hookah CRM/ERP platform.

## Structure

- `backend/HookahPlatform.sln` - .NET solution.
- `backend/services` - API Gateway and microservice hosts.
- `backend/shared/contracts` - integration events and public contracts.
- `backend/shared/building-blocks` - shared API defaults and domain rules.
- `infrastructure/docker-compose.yml` - PostgreSQL, Redis, RabbitMQ and service containers.
- `docs` - architecture, API, events and database notes.

## Build

```bash
rtk env DOTNET_CLI_HOME=/tmp dotnet build backend/HookahPlatform.sln
```

## Run One Service

```bash
rtk env DOTNET_CLI_HOME=/tmp dotnet run --project backend/services/mixology-service/src/HookahPlatform.MixologyService/HookahPlatform.MixologyService.csproj
```

## Notes

The local machine currently has .NET SDK 8.0, so projects target `net8.0`. The requested ASP.NET Core 9 upgrade should be done after installing SDK 9.

The API Gateway is the public entrypoint at `http://localhost:8080`. Service containers communicate on the internal Docker network.

## API Smoke Tests

Use [docs/smoke-tests.http](docs/smoke-tests.http) after starting Docker Compose. Requests are written against the gateway.

## Frontend

```bash
pnpm install
pnpm crm:dev
pnpm client:dev
```

CRM runs on `http://localhost:3000`. Client app runs on `http://localhost:3001`.

Docker Compose also includes `crm-app` and `client-app` services.

With Nginx enabled in Compose, `http://localhost` serves the client app, `http://localhost/crm/` serves CRM, and `http://localhost/api/` proxies API Gateway.

## Kubernetes

Base manifests live in `infrastructure/kubernetes`.
