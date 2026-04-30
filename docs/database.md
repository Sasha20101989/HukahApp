# Database Model

The PostgreSQL baseline schema is defined in `infrastructure/postgres/001_init.sql`. EF Core/Npgsql is wired for every domain service through `AddPostgresDbContext<TContext>()`; each service exposes `/persistence/health` to validate its configured PostgreSQL connection.

Service DbContexts map to these tables:

- Auth/User: `users`, `roles`, `permissions`, `role_permissions`
- User: `users`, `roles`, `permissions`, `role_permissions`, `staff_shifts`
- Branch: `branches`, `halls`, `zones`, `branch_working_hours`, `tables`, `hookahs`
- Mixology: `bowls`, `tobaccos`, `mixes`, `mix_items`
- Inventory: `inventory_items`, `inventory_movements`
- Order: `orders`, `order_items`
- Booking: `bookings`
- Payment: `payments`
- Notification: `notifications`, `notification_templates`, `notification_preferences`
- Analytics: `analytics_orders`, `analytics_bookings`, `analytics_tobacco_usage`
- Review: `reviews`
- Promo: `promocodes`, `promocode_redemptions`

The current codebase has the EF Core persistence foundation in place for all services. Some HTTP handlers still use in-memory collections while they are being migrated endpoint-by-endpoint to the service DbContexts. Keep integration through events/HTTP contracts instead of direct cross-service queries.

Database-level business guards already included in the baseline schema:

- Booking overlap protection per table with a PostgreSQL exclusion constraint.
- Mix item percent sum validation with deferred constraint triggers.
- Positive quantities and ratings through check constraints.
