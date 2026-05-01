# API Surface

The public entrypoint is the API Gateway. In Docker Compose it listens on `http://localhost:8080` and proxies the API paths below to the owning services.

Implemented service endpoints:

- Auth: `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout`, `/api/auth/roles`
- Users/Roles: `/api/users/me`, `/api/users`, `/api/users/staff`, `/api/users/clients`, `/api/users/{id}`, `/api/users/{id}/booking-eligibility`, `/api/users/{id}/permissions`, `/api/roles`, `/api/roles/{code}/permissions`, `/api/permissions`, `/api/staff/shifts`, `/api/staff/shifts/{id}/start`, `/api/staff/shifts/{id}/finish`, `/api/staff/shifts/{id}/cancel`
- Branches: `/api/branches`, `/api/branches/{id}`, `/api/branches/{id}/working-hours`, `/api/branches/{id}/floor-plan`, `/api/branches/{id}/halls`, `/api/branches/{id}/zones`, `/api/zones`, `/api/halls`, `/api/halls/{id}/tables`, `/api/tables`, `/api/tables/{id}`, `/api/tables/{id}/status`
- Hookahs: `/api/hookahs`, `/api/hookahs/{id}`, `/api/hookahs/{id}/status`
- Bowls: `/api/bowls`, `/api/bowls/{id}`
- Tobaccos: `/api/tobaccos`, `/api/tobaccos/{id}`
- Mixes: `/api/mixes`, `/api/mixes/{id}`, `/api/mixes/calculate`, `/api/mixes/recommend`, `/api/mixes/{id}/visibility`
- Inventory: `/api/inventory`, `/api/inventory/check`, `/api/inventory/in`, `/api/inventory/out`, `/api/inventory/adjustment`, `/api/inventory/movements`, `/api/inventory/dispatch-event`
- Orders: `/api/orders`, `/api/orders/status-flow`, `/api/orders/runtime/branch/{branchId}`, `/api/orders/{id}`, `/api/orders/{id}/status`, `/api/orders/{id}/payment-succeeded`, `/api/orders/{id}/assign-hookah-master`, `/api/orders/{id}/coal-change`, `/api/orders/{id}/coal-timer`
- Bookings: `/api/bookings/availability`, `/api/bookings/holds`, `/api/bookings/holds/{id}`, `/api/bookings`, `/api/bookings/{id}`, `/api/bookings/{id}/confirm`, `/api/bookings/{id}/cancel`, `/api/bookings/{id}/reschedule`, `/api/bookings/{id}/no-show`, `/api/bookings/{id}/client-arrived`, `/api/bookings/{id}/complete`, `/api/bookings/mark-expired-no-shows`
- Payments: `/api/payments/create`, `/api/payments/webhook/yookassa`, `/api/payments/{id}`, `/api/payments/{id}/refund`
- Notifications: `/api/notifications`, `/api/notifications/templates`, `/api/notifications/preferences/{userId}`, `/api/notifications/{id}/read`, `/api/notifications/send`, `/api/notifications/dispatch-event`
- Analytics: `/api/analytics/events`, `/api/analytics/dashboard`, `/api/analytics/top-mixes`, `/api/analytics/tobacco-usage`, `/api/analytics/staff-performance`, `/api/analytics/table-load`
- Reviews: `/api/reviews`, `/api/reviews/mixes/{mixId}/summary`, `/api/reviews/clients/{clientId}`
- Promo: `/api/promocodes`, `/api/promocodes/validate`, `/api/promocodes/redeem`, `/api/promocodes/{code}/deactivate`

All services also expose `/health`, `/events/debug`, `/outbox/debug`, `/outbox/dispatch` and `/persistence/health`.

Gateway-only endpoints:

- `/api/catalog/services`
- `/api/catalog/routes`

Role permissions are defined centrally in `HookahPlatform.Contracts.RolePermissionCatalog` and used by Auth/User services so JWT role codes and CRM permission views stay aligned.

Endpoint access is defined centrally in `HookahPlatform.Contracts.EndpointAccessPolicy` and enforced twice:

- API Gateway validates Bearer JWT, checks the permission matrix and forwards signed user context through `X-User-Id`, `X-User-Role`, `X-User-Permissions` and `X-Gateway-Secret`.
- Backend services validate the forwarded gateway context again and also accept trusted service-to-service calls through `X-Service-Name` and `X-Service-Secret`.
- Gateway strips inbound `X-User-*`, `X-Gateway-Secret` and `X-Service-*` headers before proxying, so clients cannot spoof identity or internal service calls.
- Internal endpoints such as `/api/analytics/events`, `/api/notifications/dispatch-event`, `/api/inventory/dispatch-event`, `/events/debug` and `/outbox/*` are not exposed through the gateway.
- Unknown non-public read endpoints require at least an authenticated user; unknown write endpoints fall back to `staff.manage`.

Public endpoints are intentionally limited to auth, service catalog, health checks, read-only branch/mix/review/availability views, YooKassa webhook and promocode validation.

Client-owned flows have additional service-side checks: clients can only read/create their own bookings, create payments for themselves and read their own user permission/eligibility data unless their role has the required staff permission.

Order status changes are restricted by the order status flow. Moving an order to `SERVED` emits `OrderServed` once and stores `inventoryWrittenOffAt`; Inventory Service consumes that event and writes off mix tobacco idempotently.

Internal service endpoints are not published as host ports by default. Use the gateway for client and CRM traffic.
