# Hookah CRM Platform Architecture

The repository follows the requested microservice layout:

- `backend/services/*` contains ASP.NET Core service hosts.
- `backend/shared/contracts` contains integration events and service catalog contracts.
- `backend/shared/building-blocks` contains shared API defaults and domain primitives.
- `infrastructure/docker-compose.yml` defines service containers plus PostgreSQL, Redis and RabbitMQ.

Current implementation is the first executable platform slice. API boundaries, endpoint names and critical business rules match the technical specification. PostgreSQL, RabbitMQ, Redis and Docker Compose are already represented in the repository; the next backend step is replacing temporary process-local repositories with EF Core persistence and RabbitMQ consumers.

The gateway uses `ServiceCatalog.Routes` so secondary API groups, such as `/api/bowls`, `/api/tobaccos` and `/api/hookahs`, are routed to the correct owning services.

Security foundation:

- Auth Service stores PBKDF2 password hashes.
- Auth Service issues HS256 JWT-compatible access tokens.
- API Gateway validates bearer tokens when present and forwards `X-User-Id` and `X-User-Role`.

Local SDK note: this workspace has .NET SDK 8.0 installed, so projects target `net8.0`. The structure is ready for moving to ASP.NET Core 9 when SDK 9 is available.
