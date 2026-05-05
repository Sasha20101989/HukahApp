# Backup And Restore

## Backup Requirements

- PostgreSQL backups are encrypted at rest and stored outside the primary cluster failure domain.
- Backup schedule must include point-in-time recovery coverage for operational data.
- Redis runtime data is treated as disposable unless a service explicitly persists equivalent state in PostgreSQL.
- RabbitMQ messages are recoverable through durable queues and the outbox table; failed delivery replay starts from persisted events.
- Secrets used for restore drills are rotated separately from production credentials.

## Restore Drill

1. Restore PostgreSQL backup into staging.
2. Run migration status check against the restored database.
3. Start services with the restored database, Redis and RabbitMQ.
4. Run tenant isolation smoke against the staging gateway.
5. Run payment webhook replay test with test provider payload.
6. Validate tenant admin console, client booking flow and analytics dashboard against restored data.
7. Record drill result, restore timestamp, backup source, migration version and operator notes.

## Acceptance Criteria

- Restored staging environment starts without manual data edits.
- Tenant isolation smoke passes.
- Payment webhook replay is idempotent.
- No client-facing page exposes internal cost/margin data.
