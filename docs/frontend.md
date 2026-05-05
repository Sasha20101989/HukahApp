# Frontend Apps

The frontend is a pnpm workspace with two Next.js PWA apps:

- `frontend/crm-app` on port `3000`
- `frontend/client-app` on port `3001`

## CRM app

Implemented staff workflows:

- tablet-first operational dashboard;
- owner-only tenant admin console at `/admin/tenants`;
- owner-only custom role editor at `/admin/roles`;
- owner-only audit log viewer at `/admin/audit`;
- role switching for `OWNER`, `MANAGER`, `HOOKAH_MASTER`, `WAITER`;
- React Query polling for branch runtime data;
- Zustand state for selected role, section, branch and search;
- floor-plan view with table states;
- active order runtime snapshots from `/api/orders/runtime/branch/{branchId}`;
- order status actions: `READY`, `SERVED`, `COMPLETED`;
- coal-change action;
- bookings board;
- inventory low-stock board;
- mixology board with internal cost/margin visible only in CRM;
- staff shift/performance board placeholder ready for richer data.

### Tenant admin console

`/admin/tenants` is protected by the CRM route middleware and by UI permission checks. It requires `tenants.manage` and is intended for platform OWNER users.

Implemented workflows:

- list tenants from `GET /api/tenants`;
- create tenant through `POST /api/tenants`;
- save tenant settings through `PUT /api/tenants/{id}/settings`;
- edit tenant name, slug and active status;
- edit default timezone, currency and deposit requirement.
- run lifecycle actions: suspend, reactivate, export snapshot and soft-delete.

The page uses the same JWT refresh flow as the CRM dashboard. It does not use demo fallback data: if backend authorization, tenant endpoints or settings endpoints fail, the page renders the backend error state.

### Role editor

`/admin/roles` is an OWNER-only UI for tenant-scoped custom roles. It loads roles and permission definitions from strict backend APIs, creates custom roles, edits active state, assigns permission checkboxes and blocks destructive actions for system roles.

### Audit log

`/admin/audit` is an OWNER-only read-only viewer for tenant-scoped audit records. It supports action, target type, actor and limit filters and renders backend metadata JSON without demo fallback.

## Client app

Implemented client workflows:

- profile registration/login flow;
- tenant branding load from `GET /api/public/tenant/branding` with CSS variables `--tenant-primary` and `--tenant-accent`;
- branch/date/time/guest selection;
- availability lookup;
- Redis-backed table hold flow through `/api/bookings/holds`;
- mix selection without internal cost/margin exposure;
- booking creation with hold id;
- deposit payment creation;
- payment return page with `SUCCESS`, `FAILED`, `PROCESSING`, missing-payment handling, polling and manual retry;
- production payment redirect contract: `Payments:CheckoutBaseUrl` enables provider redirect, local mode returns directly to `/payment/return` for development;
- booking history view;
- review submission;
- React Query data flow and Zustand booking draft/session state.

## Smoke tests

The repository includes dependency-free smoke checks for local frontend QA:

- `corepack pnpm frontend:smoke` checks server responses, middleware redirects, manifests and payment return routes with `fetch`.
- `corepack pnpm frontend:browser-smoke` renders CRM/client/payment/account guard pages in headless Chrome/Chromium and fails on missing markers or Next.js runtime error markers.

Expected local defaults are `CRM_URL=http://localhost:3000` and `CLIENT_URL=http://localhost:3001`. Override them for Docker/Nginx or deployed environments.

## Commands

```bash
corepack pnpm install
corepack pnpm crm:dev
corepack pnpm client:dev
corepack pnpm frontend:build
```

The root scripts intentionally call `corepack pnpm` so the workspace does not depend on a globally installed `pnpm` binary.
