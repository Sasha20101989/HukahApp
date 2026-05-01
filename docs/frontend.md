# Frontend Apps

The frontend is a pnpm workspace with two Next.js PWA apps:

- `frontend/crm-app` on port `3000`
- `frontend/client-app` on port `3001`

## CRM app

Implemented staff workflows:

- tablet-first operational dashboard;
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

## Client app

Implemented client workflows:

- profile registration/login flow;
- branch/date/time/guest selection;
- availability lookup;
- Redis-backed table hold flow through `/api/bookings/holds`;
- mix selection without internal cost/margin exposure;
- booking creation with hold id;
- deposit payment creation;
- booking history view;
- review submission;
- React Query data flow and Zustand booking draft/session state.

## Commands

```bash
corepack pnpm install
corepack pnpm crm:dev
corepack pnpm client:dev
corepack pnpm frontend:build
```

The root scripts intentionally call `corepack pnpm` so the workspace does not depend on a globally installed `pnpm` binary.
