# Database Model

The MVP currently uses in-memory collections per service. The target PostgreSQL tables from the specification map directly to the records in service `Program.cs` files:

- Auth/User: `users`, `roles`, `permissions`, `role_permissions`
- Branch: `branches`, `halls`, `tables`, `hookahs`
- Mixology: `bowls`, `tobaccos`, `mixes`, `mix_items`
- Inventory: `inventory_items`, `inventory_movements`
- Order: `orders`, `order_items`
- Booking: `bookings`
- Payment: `payments`
- Review: `reviews`

Recommended next step is one database per service schema with EF Core migrations, keeping integration through events instead of direct cross-service queries.
