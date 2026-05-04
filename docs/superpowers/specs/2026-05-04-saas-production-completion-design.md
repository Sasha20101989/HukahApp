# Hookah CRM Platform - SaaS Production Completion Spec

Date: 2026-05-04

## Purpose

This document defines what remains to move the current Hookah CRM Platform from an executable local platform slice to a production-ready multi-tenant SaaS product.

It does not replace the original product requirements. It narrows the remaining production work into architecture decisions, implementation gaps, release phases, and acceptance criteria.

## Confirmed Production Direction

- Product model: multi-tenant SaaS for many independent hookah companies and branch networks.
- Database model: shared PostgreSQL database with mandatory `tenant_id` isolation on tenant-owned data.
- Authorization model: tenant-owned custom roles backed by a system permission catalog.
- Payment model: each tenant owns and manages its payment provider credentials.
- Infrastructure target: managed Kubernetes production deployment.

## Current Implementation Baseline

The repository already contains a substantial executable platform slice:

- Backend services: auth, user, branch, mixology, inventory, order, booking, payment, notification, analytics, review, promo, and API gateway.
- PostgreSQL persistence through service DbContexts.
- Centralized EF Core migration project under `backend/infrastructure/migrations`.
- Redis-backed runtime state for refresh-session indexing, booking holds, order runtime snapshots, and coal timers.
- RabbitMQ/event-driven integration with outbox/fallback patterns documented.
- OpenTelemetry/Serilog observability setup and local collector documentation.
- CRM and client Next.js PWA applications in a pnpm workspace.
- CRM CRUD, booking, order, inventory, mixology, staff, notification, analytics, review, and promo screens.
- Client booking/account/payment-return flows.
- Local full-stack Docker Compose startup scripts.
- API, frontend, and browser smoke scripts.

This baseline is not yet production-ready SaaS because it lacks tenant isolation, tenant lifecycle, SaaS security hardening, Kubernetes production manifests, complete CI/CD, and full tenant-aware testing.

## Target SaaS Architecture

### Tenant Context

Tenant context is a mandatory cross-cutting concern.

The API Gateway resolves the tenant from one of the supported production inputs:

- tenant subdomain;
- custom tenant domain;
- authenticated session claims;
- explicit internal service header for trusted service-to-service calls.

The gateway passes the resolved tenant as `X-Tenant-Id`, but services must independently validate tenant context. No domain service may rely only on gateway enforcement.

### Tenant-Owned Data

All tenant-owned tables require a non-null `tenant_id`:

- users and user profile data;
- roles, permissions assignments, staff branch scope;
- branches, halls, zones, tables, hookahs;
- bowls, tobaccos, mixes, mix items;
- inventory items and movements;
- bookings and booking holds;
- orders, order items, runtime state, coal timers;
- payments, refunds, provider credentials;
- notifications, templates, preferences;
- reviews;
- promocodes and redemptions;
- analytics source facts and aggregates.

Shared platform tables may remain tenantless only when they represent global system metadata, such as the system permission catalog or platform-wide migration metadata.

### PostgreSQL Isolation

The production database remains shared, but isolation is explicit:

- every tenant-owned query filters by `tenant_id`;
- write commands reject mismatched tenant identifiers;
- key indexes include `tenant_id` first where query patterns are tenant-scoped;
- uniqueness constraints that are tenant-owned become composite constraints with `tenant_id`;
- high-risk tables should use PostgreSQL row-level security after service-level filters are in place;
- migration tests verify that legacy rows are assigned to a seed/system tenant.

### Redis Isolation

Redis keys are namespaced by tenant:

- `tenant:{tenantId}:auth:session:{sessionId}`;
- `tenant:{tenantId}:booking:hold:{branchId}:{tableId}:{slot}`;
- `tenant:{tenantId}:order:runtime:{branchId}`;
- `tenant:{tenantId}:coal:{orderId}`;

Cache invalidation must operate within tenant scope. Shared cache keys require an explicit `platform:` namespace.

### RabbitMQ and Events

All integration events carry:

- `tenantId`;
- `eventId`;
- `correlationId`;
- `causationId`;
- `occurredAt`;
- event schema version.

Consumers must reject events without tenant context unless the event is explicitly platform-scoped.

### Custom RBAC

The platform keeps a system permission catalog, but each tenant owns its roles.

Tenant admins can:

- create and edit roles;
- assign permissions to roles;
- assign users to roles;
- scope staff access to one or more branches;
- deactivate roles that are no longer used.

Services enforce permissions from claims and service-side tenant lookups. UI-level RBAC is only a usability layer, not the security boundary.

### Tenant-Owned Payments

Each tenant configures its own payment provider credentials.

Payment requirements:

- payment creation reads provider settings by tenant;
- webhook routing resolves tenant safely from provider account, webhook secret, external payment id, or signed metadata;
- webhook handlers reject events that do not match the tenant and provider configuration;
- a payment cannot confirm a booking or order owned by another tenant;
- secrets are encrypted at rest and never returned to frontend clients.

### Kubernetes Production Target

Production deployment targets managed Kubernetes:

- Helm charts or equivalent reproducible manifests;
- ingress with TLS automation;
- service-level probes;
- resource requests and limits;
- horizontal pod autoscaling where useful;
- Kubernetes Job for migrations;
- secrets through an external secret manager;
- managed PostgreSQL, Redis, and RabbitMQ or compatible cloud services;
- OpenTelemetry collector and dashboards.

## Production Gap Matrix

| Area | Current State | Required Production State | Priority |
| --- | --- | --- | --- |
| Tenant model | Not implemented as first-class SaaS concept | Tenants, tenant settings, tenant lifecycle, tenant resolution | Critical |
| Data isolation | Shared schema without universal `tenant_id` | `tenant_id` on tenant-owned tables, tenant filters, composite indexes, RLS for critical tables | Critical |
| RBAC | Roles/permissions exist, mostly fixed around current role model | Tenant-owned custom roles, permission editor, branch-scoped assignments | Critical |
| API gateway | Routes and permissions exist | Tenant resolver, tenant headers, tenant-aware policy enforcement | Critical |
| Service security | Some service-side validation exists | Every service validates `X-User-*` and tenant context on reads/writes | Critical |
| Payments | Payment service and return flow exist | Tenant-owned credentials, tenant-safe webhook routing, encrypted secrets | Critical |
| Frontend CRM | Broad operational UI exists | Tenant admin console, role editor, tenant-aware navigation, decomposed modules | High |
| Client app | Booking/account/payment flows exist | Tenant branding, tenant-domain routing, production client history/reviews/preferences | High |
| Redis | Used for selected runtime state | Tenant-namespaced keys and tenant-scoped invalidation | High |
| RabbitMQ/events | Outbox/events documented and partially implemented | Tenant-aware event schemas, consumer rejection for missing tenant | High |
| Analytics | Dashboards exist | Tenant-safe facts, aggregates, platform-admin separation | High |
| Notifications | Center/templates/preferences exist | Tenant-owned channels, credentials, templates, delivery audit | High |
| Kubernetes | Docker Compose local stack exists | Managed K8s manifests/Helm, probes, autoscaling, migrations job | High |
| CI/CD | Scripts exist | Full pipeline with build, tests, migrations validation, image publishing, deployment gates | High |
| Observability | Shared observability setup exists | Tenant/correlation-aware logs, traces, metrics, dashboards, alerts | High |
| Tests | Smoke tests exist | Tenant isolation, payment webhook, migration, browser e2e, rollback and load tests | High |
| Security | Basic auth/RBAC exists | rate limits, audit logs, CSP, secure token policy, backup/restore drill | High |
| Data lifecycle | Not production-complete | tenant export, retention, deletion/anonymization, backup restore | Medium |
| Billing/tariffs | Not implemented | SaaS subscription and feature plans if commercialized as paid platform | Medium |

## Release Phases

### Phase 0 - Stabilize Current Platform

Goal: make the current single-tenant-capable slice stable before layering SaaS isolation.

Scope:

- keep backend and frontend builds green;
- keep Docker Compose startup reliable;
- keep smoke scripts passing;
- reduce oversized frontend modules where they block safe changes;
- remove demo fallbacks from production paths;
- document current operational flows clearly.

Acceptance:

- `dotnet build backend/HookahPlatform.sln --no-restore -m:1 /nr:false` passes;
- `corepack pnpm frontend:build` passes;
- API and frontend smoke scripts pass against local stack;
- no generated npm lockfile exists in the pnpm workspace.

### Phase 1 - Tenant Foundation

Goal: introduce tenant identity and tenant-safe persistence.

Scope:

- add tenant entities and tenant settings;
- add tenant resolution in gateway;
- add `tenant_id` migrations for tenant-owned data;
- assign existing seed data to a system/demo tenant;
- add service-side tenant validation;
- namespace Redis keys;
- add `tenantId` to integration events.

Acceptance:

- a request without a valid tenant is rejected for tenant-owned endpoints;
- a user from tenant A cannot access tenant B data through API calls;
- migrations backfill tenant data deterministically;
- tenant isolation integration tests pass.

### Phase 2 - SaaS Security and Custom RBAC

Goal: replace fixed operational assumptions with tenant-owned custom access control.

Scope:

- system permission catalog remains platform-owned;
- tenant roles become tenant-owned;
- tenant admin can create/edit/deactivate roles;
- branch-scoped staff assignments are enforced;
- audit log records security-sensitive actions;
- rate limits and session policy are tenant-aware;
- high-risk tables adopt PostgreSQL RLS after service filters are proven.

Acceptance:

- tenant admin can manage roles without code changes;
- permission changes take effect after token refresh or explicit session refresh;
- audit records include tenant, actor, action, target, result, and correlation id.

### Phase 3 - Tenant Payments and Notifications

Goal: make money and customer communication tenant-safe.

Scope:

- tenant-owned payment provider credentials;
- encrypted credential storage;
- webhook tenant routing and signature validation;
- tenant-owned notification channels and templates;
- delivery audit and retry policy;
- payment/refund actions tied to tenant-owned bookings and orders.

Acceptance:

- webhook for tenant A cannot mutate tenant B payment/order/booking;
- provider secrets are never exposed to frontend;
- notification delivery can be audited per tenant and channel.

### Phase 4 - Production Frontend

Goal: make CRM and client apps production-grade for SaaS operators and end users.

Scope:

- CRM module decomposition by domain;
- tenant admin console;
- custom role and permission editor;
- tenant/branch switcher;
- tenant-aware branding for client app;
- role-aware navigation and route protection;
- complete loading, empty, error, and mutation states;
- browser e2e coverage for critical flows.

Acceptance:

- frontend has no production demo fallback;
- users see only tenant/role-allowed flows;
- critical journeys pass browser e2e tests: login, role management, branch setup, mix creation, inventory, booking, payment, order serve/write-off, refund, review.

### Phase 5 - Kubernetes, CI/CD, and Operations

Goal: deploy and operate the platform reproducibly.

Scope:

- Helm charts or equivalent manifests;
- ingress and TLS;
- service probes;
- autoscaling and resource limits;
- migrations job;
- image build and publish pipeline;
- deployment gates;
- managed PostgreSQL/Redis/RabbitMQ wiring;
- backup and restore procedures;
- observability dashboards and alerts.

Acceptance:

- a clean Kubernetes environment can deploy the platform from manifests and secrets;
- migrations run once and fail safely;
- failed deployment does not corrupt data;
- alerts cover API errors, payment webhook failures, queue lag, database health, and latency.

### Phase 6 - SaaS Hardening

Goal: complete operational SaaS lifecycle requirements.

Scope:

- tenant onboarding/offboarding;
- tenant data export;
- retention policy;
- deletion/anonymization workflow;
- platform-admin console;
- load testing;
- security testing;
- optional billing/tariffs.

Acceptance:

- tenant data can be exported and deleted according to policy;
- platform admin can support tenants without direct database access;
- load and security tests meet agreed thresholds.

## Production Acceptance Criteria

The platform is production-ready SaaS when all of the following are true:

- Tenant isolation is enforced in UI, API, database access, Redis keys, and events.
- Tenant A cannot read, mutate, pay for, confirm, refund, notify, or analyze tenant B data.
- Tenant admins manage custom roles and branch scopes without code changes.
- Payment provider credentials are tenant-owned, encrypted, and used only within tenant scope.
- Webhooks are signature-validated and tenant-routed.
- CRM and client apps have route guards, refresh token behavior, role-aware navigation, and complete error/loading/empty states.
- Production deployment is reproducible in managed Kubernetes.
- CI/CD builds, tests, validates migrations, publishes images, and gates deployments.
- Logs, traces, and metrics include tenant and correlation identifiers.
- Dashboards and alerts cover business-critical and infrastructure-critical signals.
- Backup and restore are documented and tested.
- E2E tests cover tenant isolation and critical business flows.
- Secrets are not committed to the repository and are injected through production secret management.

## Testing Strategy

Required test layers:

- Unit tests for domain rules such as mix percentages, inventory write-off, booking overlap, RBAC evaluation, and payment state transitions.
- Integration tests for service persistence, tenant filters, migrations, Redis namespacing, and RabbitMQ event handling.
- API smoke tests against the gateway for all critical endpoints.
- Browser e2e tests for CRM and client flows.
- Tenant isolation tests that intentionally attempt cross-tenant reads/writes.
- Payment webhook tests for valid, invalid, replayed, and cross-tenant events.
- Migration tests from the current baseline to tenant-aware schema.
- Backup/restore drill in a staging-like environment.
- Load tests for booking availability, order runtime board, payment webhooks, and analytics reads.

## Security Requirements

- Use short-lived access tokens and refresh token rotation.
- Store refresh tokens server-side and support forced tenant/user logout.
- Apply rate limiting to auth, booking, payment, and public endpoints.
- Enforce webhook secrets/signatures in production.
- Add audit logs for auth, role changes, payment actions, refunds, inventory adjustments, and tenant settings.
- Apply CSP and secure headers in frontend and gateway.
- Keep provider credentials and secrets outside repo and encrypted at rest.
- Ensure service-to-service calls use trusted internal authentication.

## Data Lifecycle Requirements

- Tenant export produces a complete tenant-scoped dataset.
- Tenant deletion either hard-deletes or anonymizes data according to configured retention policy.
- Backups are encrypted and restorable.
- Restore drills verify application compatibility with restored data.
- Analytics retention is explicit and tenant-scoped.

## Out of Scope for the First SaaS Production Cut

These items are valuable but should not block the first production SaaS release:

- database-per-tenant isolation;
- schema-per-tenant isolation;
- marketplace split payouts;
- advanced ABAC policy engine;
- public plugin marketplace;
- native mobile applications;
- AI mix recommendations beyond deterministic filtering;
- full enterprise SSO unless required by first paying tenants.

## Key Risks

- Tenant retrofitting can introduce data leaks if service filters are inconsistent.
- Shared database indexes may need careful tuning as tenants grow.
- Payment webhook routing is high-risk because one bug can confirm or refund the wrong tenant data.
- Large frontend modules can slow safe iteration unless decomposed during Phase 0 and Phase 4.
- Kubernetes work can hide product bugs if started before tenant isolation tests exist.

## Recommended Immediate Next Step

Create an implementation plan for Phase 0 and Phase 1 only.

Reasoning:

- Phase 0 stabilizes the current worktree and prevents regression.
- Phase 1 creates the tenant foundation required by every later SaaS phase.
- Phases 2-6 depend on tenant identity and cannot be implemented safely before tenant isolation exists.
