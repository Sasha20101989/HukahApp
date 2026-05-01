# Hookah CRM Platform Architecture

The repository follows the requested microservice layout:

- `backend/services/*` contains ASP.NET Core service hosts.
- `backend/shared/contracts` contains integration events and service catalog contracts.
- `backend/shared/building-blocks` contains shared API defaults and domain primitives.
- `backend/infrastructure/migrations` contains the centralized EF Core migration runner.
- `infrastructure/docker-compose.yml` defines service containers plus PostgreSQL, Redis, RabbitMQ and the OpenTelemetry Collector.

Current implementation is an executable platform slice. API boundaries, endpoint names and critical business rules match the technical specification. PostgreSQL persistence, centralized EF Core migrations, transactional outbox, RabbitMQ publishing, RabbitMQ consumers for analytics/notifications, HTTP fallback fan-out, Redis-backed runtime cache, shared observability and Docker Compose wiring are represented in the repository.

Database migrations:

- `HookahPlatform.Migrations` is the single migration project for the shared PostgreSQL database.
- Docker Compose runs `db-migrator` after PostgreSQL is healthy and before backend services start.
- The initial EF migration embeds the previous `001_init.sql` baseline and records it in EF migration history.
- Service projects do not own separate migrations while they share one database.

Redis runtime state:

- All services get `IDistributedCache` from shared building blocks.
- If `Redis__Enabled=true`, services use Redis through `ConnectionStrings__Redis`.
- If Redis is disabled, services fall back to in-memory distributed cache for local development.
- Auth Service stores a Redis refresh-session index on top of PostgreSQL refresh tokens for fast session lookup/rotation/logout.
- Booking Service caches branch tables, working hours and short-lived availability snapshots, while booking intersection checks still use PostgreSQL.
- Booking Service stores 10-minute table hold slots in Redis and excludes them from availability.
- Order Service stores coal timer runtime state in Redis and keeps coal-change history in PostgreSQL.
- Order Service stores active CRM order snapshots in Redis for fast branch runtime views.
- Branch Service invalidates booking cache entries when branch floor-plan or working-hours data changes and mirrors table/hookah runtime state.

Event-driven inventory:

- Order Service emits `OrderServed` when an order reaches `SERVED`.
- Inventory Service consumes `OrderServed`, fetches mix composition from Mixology Service and writes off tobacco.
- Write-off processing is idempotent by integration event id and by `orderId + tobaccoId` movement checks.

Observability:

- Shared `AddHookahServiceDefaults()` wires Serilog structured console logs for every backend host.
- Shared `UseHookahServiceDefaults()` adds `X-Correlation-Id` propagation and Serilog request logging.
- OpenTelemetry resource metadata includes `service.name`, `service.version`, `service.instance.id` and deployment environment.
- OpenTelemetry traces cover ASP.NET Core inbound requests and `HttpClient` service-to-service calls.
- OpenTelemetry metrics cover ASP.NET Core, `HttpClient` and custom Hookah Platform meters.
- Service-side access denials increment `hookah_access_denied_total` with reason/path tags.
- Docker Compose exports OTLP to `otel-collector` on `4317/4318`; collector exposes Prometheus metrics on `http://localhost:9464/metrics` and debug output in collector logs.

The gateway uses `ServiceCatalog.Routes` so secondary API groups, such as `/api/bowls`, `/api/tobaccos` and `/api/hookahs`, are routed to the correct owning services.

Security foundation:

- Auth Service stores PBKDF2 password hashes.
- Auth Service issues HS256 JWT-compatible access tokens.
- `EndpointAccessPolicy` in shared contracts is the single permission matrix for gateway and services.
- API Gateway validates bearer tokens, blocks internal endpoints, strips spoofable forwarding headers and forwards only trusted `X-User-Id`, `X-User-Role`, `X-User-Permissions` plus `X-Gateway-Secret`.
- Every backend service runs `UseServiceAccessControl()` and revalidates gateway context before protected handlers execute.
- Service-to-service HTTP calls are signed automatically with `X-Service-Name` and `X-Service-Secret`, so internal consumers and cross-service business calls do not rely on client JWTs.
- Public API surface is explicit: auth, catalog, health, public branch/mix/review reads, booking availability, payment webhook and promocode validation.
- Unknown non-public reads require authentication; unknown writes require `staff.manage`.
- User, Booking and Payment services also enforce owner checks for client-owned data using `X-User-Id`, not only gateway route permissions.

Local SDK note: this workspace has .NET SDK 8.0 installed, so projects target `net8.0`. The structure is ready for moving to ASP.NET Core 9 when SDK 9 is available.
