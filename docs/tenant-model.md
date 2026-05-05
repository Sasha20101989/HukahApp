# Tenant Model

## States

The current database stores tenant lifecycle through `tenants.is_active`. The product lifecycle maps to the existing schema as follows until a dedicated `status` column is introduced:

- `ACTIVE`: `is_active=true`; tenant can operate normally.
- `SUSPENDED`: `is_active=false`; public tenant branding no longer resolves and business access should be blocked at the gateway/service boundary.
- `DELETING`: operational state used during export/deletion workflows; represented by a suspended tenant while retention jobs run.
- `DELETED`: tenant is no longer active; retained records must be anonymized or removed according to the retention policy before physical deletion is enabled.

## Lifecycle Endpoints

- `PATCH /api/tenants/{id}/suspend`: marks tenant inactive.
- `PATCH /api/tenants/{id}/reactivate`: marks tenant active.
- `POST /api/tenants/{id}/export`: returns tenant metadata and settings snapshot for backup/export workflows.
- `DELETE /api/tenants/{id}`: soft-deletes by marking tenant inactive. Hard deletion is intentionally deferred until retention/export jobs are implemented.

All lifecycle endpoints are protected by `tenants.manage` through the gateway permission matrix.
