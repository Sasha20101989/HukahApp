# Database Model

The PostgreSQL baseline schema is defined in `infrastructure/postgres/001_init.sql`. Service records and request models map to these tables:

- Auth/User: `users`, `roles`, `permissions`, `role_permissions`
- Branch: `branches`, `halls`, `tables`, `hookahs`
- Mixology: `bowls`, `tobaccos`, `mixes`, `mix_items`
- Inventory: `inventory_items`, `inventory_movements`
- Order: `orders`, `order_items`
- Booking: `bookings`
- Payment: `payments`
- Review: `reviews`

Recommended persistence direction is one schema or database per service with EF Core migrations, keeping integration through events instead of direct cross-service queries.

Database-level business guards already included in the baseline schema:

- Booking overlap protection per table with a PostgreSQL exclusion constraint.
- Mix item percent sum validation with deferred constraint triggers.
- Positive quantities and ratings through check constraints.
