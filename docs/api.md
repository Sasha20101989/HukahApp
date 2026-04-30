# API Surface

Implemented service endpoints:

- Auth: `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout`, `/api/auth/roles`
- Users: `/api/users/me`, `/api/users`, `/api/users/staff`, `/api/users/{id}`
- Branches: `/api/branches`, `/api/branches/{id}`, `/api/branches/{id}/halls`, `/api/halls`, `/api/halls/{id}/tables`, `/api/tables`, `/api/tables/{id}/status`
- Hookahs: `/api/hookahs`, `/api/hookahs/{id}/status`
- Bowls: `/api/bowls`, `/api/bowls/{id}`
- Tobaccos: `/api/tobaccos`, `/api/tobaccos/{id}`
- Mixes: `/api/mixes`, `/api/mixes/{id}`, `/api/mixes/calculate`, `/api/mixes/recommend`, `/api/mixes/{id}/visibility`
- Inventory: `/api/inventory`, `/api/inventory/in`, `/api/inventory/out`, `/api/inventory/adjustment`, `/api/inventory/movements`
- Orders: `/api/orders`, `/api/orders/{id}`, `/api/orders/{id}/status`, `/api/orders/{id}/assign-hookah-master`, `/api/orders/{id}/coal-change`
- Bookings: `/api/bookings/availability`, `/api/bookings`, `/api/bookings/{id}`, `/api/bookings/{id}/confirm`, `/api/bookings/{id}/cancel`, `/api/bookings/{id}/reschedule`, `/api/bookings/{id}/no-show`
- Payments: `/api/payments/create`, `/api/payments/webhook/yookassa`, `/api/payments/{id}`, `/api/payments/{id}/refund`
- Notifications: `/api/notifications`, `/api/notifications/{id}/read`, `/api/notifications/send`
- Analytics: `/api/analytics/dashboard`, `/api/analytics/top-mixes`, `/api/analytics/tobacco-usage`, `/api/analytics/staff-performance`, `/api/analytics/table-load`
- Reviews: `/api/reviews`
- Promo: `/api/promocodes`, `/api/promocodes/validate`

All services also expose `/health` and `/events/debug`.
