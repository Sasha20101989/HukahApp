# Database Model

The PostgreSQL baseline schema is defined in `infrastructure/postgres/001_init.sql`. EF Core/Npgsql is wired for every domain service through `AddPostgresDbContext<TContext>()`; each service exposes `/persistence/health` to validate its configured PostgreSQL connection.

Service DbContexts map to these tables:

- Shared events: `integration_outbox`, `processed_integration_events`
- Auth/User: `users`, `roles`, `permissions`, `role_permissions`, `refresh_tokens`
- User: `users`, `roles`, `permissions`, `role_permissions`, `staff_shifts`
- Branch: `branches`, `halls`, `zones`, `branch_working_hours`, `tables`, `hookahs`
- Mixology: `bowls`, `tobaccos`, `mixes`, `mix_items`
- Inventory: `inventory_items`, `inventory_movements`
- Order: `orders`, `order_items`, `coal_changes`
- Booking: `bookings`
- Payment: `payments`
- Notification: `notifications`, `notification_templates`, `notification_preferences`
- Analytics: `analytics_orders`, `analytics_bookings`, `analytics_tobacco_usage`
- Review: `reviews`
- Promo: `promocodes`, `promocode_redemptions`

The current codebase uses EF Core handlers for domain state across all backend services. Cross-service checks still go through HTTP contracts instead of direct DbContext access.

Every service DbContext also maps `integration_outbox`. Event-producing handlers add outbox messages to the same DbContext before `SaveChangesAsync`, so domain state and the pending integration event are committed atomically inside the service database operation.

Database-level business guards already included in the baseline schema:

- Booking overlap protection per table with a PostgreSQL exclusion constraint.
- Mix item percent sum validation with deferred constraint triggers.
- Positive quantities and ratings through check constraints.
