# Client App

Next.js client-facing booking application.

Implemented workflow:

- Select date, time and guests.
- Pick available table.
- Pick mix.
- Create booking through API Gateway.
- Create deposit payment through API Gateway.

Run:

```bash
pnpm --dir frontend/client-app install
pnpm --dir frontend/client-app dev
```

Environment:

```bash
NEXT_PUBLIC_API_BASE_URL=http://localhost:8080
```
