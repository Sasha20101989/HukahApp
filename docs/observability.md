# Observability

All backend hosts use the shared observability setup from `HookahPlatform.BuildingBlocks`.

## Logs

- Serilog writes structured JSON-like console properties.
- Every log event includes `service.name` and `deployment.environment`.
- HTTP request logs include method, path, status, elapsed time, `CorrelationId`, forwarded `UserId`, forwarded `UserRole` and remote IP.
- Incoming `X-Correlation-Id` is preserved; otherwise the service creates one from the current trace id/request id.
- The correlation id is returned in the response header `X-Correlation-Id`.

## Traces

OpenTelemetry traces are enabled for:

- ASP.NET Core inbound requests;
- `HttpClient` service-to-service calls;
- custom `HookahPlatform` activity source.

Traces are exported through OTLP. In Docker Compose services use:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

## Metrics

OpenTelemetry metrics are enabled for:

- ASP.NET Core request metrics;
- `HttpClient` metrics;
- custom `HookahPlatform` meter.

Current custom metric:

- `hookah_access_denied_total` - service-side access control denials tagged by reason and path.

The local OpenTelemetry Collector exposes Prometheus-format metrics at:

```text
http://localhost:9464/metrics
```

## Local Collector

Docker Compose starts `otel-collector` with `infrastructure/otel-collector/config.yml`.

Collector ports:

- `4317` - OTLP/gRPC receiver;
- `4318` - OTLP/HTTP receiver;
- `9464` - Prometheus metrics exporter.

For local debugging, collector logs also include debug exporter output for traces and metrics. Application logs are written directly by Serilog to each service container stdout.
