# Hookah CRM Platform Architecture

The repository follows the requested microservice layout:

- `backend/services/*` contains ASP.NET Core service hosts.
- `backend/shared/contracts` contains integration events and service catalog contracts.
- `backend/shared/building-blocks` contains shared API defaults and domain primitives.
- `infrastructure/docker-compose.yml` defines service containers plus PostgreSQL, Redis and RabbitMQ.

Current implementation is an executable MVP skeleton. Service data is in-memory, while API boundaries, endpoint names and critical business rules match the technical specification. PostgreSQL/EF Core, RabbitMQ transport, Redis caching and authentication hardening are the next infrastructure layer.

Local SDK note: this workspace has .NET SDK 8.0 installed, so projects target `net8.0`. The structure is ready for moving to ASP.NET Core 9 when SDK 9 is available.
