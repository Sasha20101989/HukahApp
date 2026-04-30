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
- Inventory: `/api/inventory`, `/api/inventory/check`, `/api/inventory/in`, `/api/inventory/out`, `/api/inventory/adjustment`, `/api/inventory/movements`
- Orders: `/api/orders`, `/api/orders/status-flow`, `/api/orders/{id}`, `/api/orders/{id}/status`, `/api/orders/{id}/payment-succeeded`, `/api/orders/{id}/assign-hookah-master`, `/api/orders/{id}/coal-change`, `/api/orders/{id}/coal-timer`
- Bookings: `/api/bookings/availability`, `/api/bookings`, `/api/bookings/{id}`, `/api/bookings/{id}/confirm`, `/api/bookings/{id}/cancel`, `/api/bookings/{id}/reschedule`, `/api/bookings/{id}/no-show`, `/api/bookings/{id}/client-arrived`, `/api/bookings/{id}/complete`, `/api/bookings/mark-expired-no-shows`
- Payments: `/api/payments/create`, `/api/payments/webhook/yookassa`, `/api/payments/{id}`, `/api/payments/{id}/refund`
- Notifications: `/api/notifications`, `/api/notifications/templates`, `/api/notifications/preferences/{userId}`, `/api/notifications/{id}/read`, `/api/notifications/send`, `/api/notifications/dispatch-event`
- Analytics: `/api/analytics/events`, `/api/analytics/dashboard`, `/api/analytics/top-mixes`, `/api/analytics/tobacco-usage`, `/api/analytics/staff-performance`, `/api/analytics/table-load`
- Reviews: `/api/reviews`, `/api/reviews/mixes/{mixId}/summary`, `/api/reviews/clients/{clientId}`
- Promo: `/api/promocodes`, `/api/promocodes/validate`, `/api/promocodes/redeem`, `/api/promocodes/{code}/deactivate`

All services also expose `/health`, `/events/debug` and `/persistence/health`.

Gateway-only endpoints:

- `/api/catalog/services`
- `/api/catalog/routes`

Role permissions are defined centrally in `HookahPlatform.Contracts.RolePermissionCatalog` and used by Auth/User services so JWT role codes and CRM permission views stay aligned.

API Gateway validates Bearer JWT for mutating routes and forwards `X-User-Id`, `X-User-Role` and `X-User-Permissions` to upstream services. Read routes, auth routes, service catalog, health checks, YooKassa webhook and promocode validation stay public for local/demo flows.

Order status changes are restricted by the order status flow. Moving an order to `SERVED` writes off mix tobacco once and stores `inventoryWrittenOffAt`; repeating the same `SERVED` status is idempotent and does not write off inventory again.

Internal service endpoints are not published as host ports by default. Use the gateway for client and CRM traffic.
