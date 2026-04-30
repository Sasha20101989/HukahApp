# API Surface

The public entrypoint is the API Gateway. In Docker Compose it listens on `http://localhost:8080` and proxies the API paths below to the owning services.

Implemented service endpoints:

- Auth: `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout`, `/api/auth/roles`
- Users: `/api/users/me`, `/api/users`, `/api/users/staff`, `/api/users/{id}`, `/api/staff/shifts`, `/api/staff/shifts/{id}/start`, `/api/staff/shifts/{id}/finish`, `/api/staff/shifts/{id}/cancel`
- Branches: `/api/branches`, `/api/branches/{id}`, `/api/branches/{id}/working-hours`, `/api/branches/{id}/floor-plan`, `/api/branches/{id}/halls`, `/api/branches/{id}/zones`, `/api/zones`, `/api/halls`, `/api/halls/{id}/tables`, `/api/tables`, `/api/tables/{id}/status`
- Hookahs: `/api/hookahs`, `/api/hookahs/{id}/status`
- Bowls: `/api/bowls`, `/api/bowls/{id}`
- Tobaccos: `/api/tobaccos`, `/api/tobaccos/{id}`
- Mixes: `/api/mixes`, `/api/mixes/{id}`, `/api/mixes/calculate`, `/api/mixes/recommend`, `/api/mixes/{id}/visibility`
- Inventory: `/api/inventory`, `/api/inventory/check`, `/api/inventory/in`, `/api/inventory/out`, `/api/inventory/adjustment`, `/api/inventory/movements`
- Orders: `/api/orders`, `/api/orders/{id}`, `/api/orders/{id}/status`, `/api/orders/{id}/assign-hookah-master`, `/api/orders/{id}/coal-change`, `/api/orders/{id}/coal-timer`
- Bookings: `/api/bookings/availability`, `/api/bookings`, `/api/bookings/{id}`, `/api/bookings/{id}/confirm`, `/api/bookings/{id}/cancel`, `/api/bookings/{id}/reschedule`, `/api/bookings/{id}/no-show`
- Payments: `/api/payments/create`, `/api/payments/webhook/yookassa`, `/api/payments/{id}`, `/api/payments/{id}/refund`
- Notifications: `/api/notifications`, `/api/notifications/templates`, `/api/notifications/preferences/{userId}`, `/api/notifications/{id}/read`, `/api/notifications/send`, `/api/notifications/dispatch-event`
- Analytics: `/api/analytics/events`, `/api/analytics/dashboard`, `/api/analytics/top-mixes`, `/api/analytics/tobacco-usage`, `/api/analytics/staff-performance`, `/api/analytics/table-load`
- Reviews: `/api/reviews`, `/api/reviews/mixes/{mixId}/summary`, `/api/reviews/clients/{clientId}`
- Promo: `/api/promocodes`, `/api/promocodes/validate`, `/api/promocodes/redeem`, `/api/promocodes/{code}/deactivate`

All services also expose `/health` and `/events/debug`.

Gateway-only endpoints:

- `/api/catalog/services`
- `/api/catalog/routes`

Internal service endpoints are not published as host ports by default. Use the gateway for client and CRM traffic.
