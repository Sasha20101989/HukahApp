# Nginx

Nginx edge routing configuration belongs here when the gateway is placed behind a reverse proxy.

`nginx.conf` routes:

- `/api/*` to API Gateway.
- `/crm/*` to CRM app.
- `/` to client app.

Service discovery is exposed by `api-gateway` at `/api/catalog/services` and `/api/catalog/routes`.
