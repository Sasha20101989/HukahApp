using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HookahPlatform.BuildingBlocks;

public static class Observability
{
    public const string ActivitySourceName = "HookahPlatform";
    public const string MeterName = "HookahPlatform";
    public const string CorrelationIdHeader = "X-Correlation-Id";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");
    public static readonly Counter<long> AccessDeniedCounter = Meter.CreateCounter<long>(
        "hookah_access_denied_total",
        description: "Number of service-side access control denials.");

    public static WebApplicationBuilder AddHookahObservability(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((context, _, logger) =>
        {
            logger
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service.name", serviceName)
                .Enrich.WithProperty("deployment.environment", context.HostingEnvironment.EnvironmentName)
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        });

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureResource(resource, serviceName, builder.Configuration))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = context => !IsNoisePath(context.Request.Path);
                    })
                    .AddHttpClientInstrumentation(options => options.RecordException = true)
                    .AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter();
            });

        return builder;
    }

    public static IApplicationBuilder UseHookahObservability(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var correlationId = GetOrCreateCorrelationId(context);
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            Activity.Current?.SetTag("correlation_id", correlationId);

            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next();
            }
        });

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.GetLevel = (httpContext, elapsed, exception) =>
                exception is not null || httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : elapsed >= 1000 || httpContext.Response.StatusCode >= 400
                        ? LogEventLevel.Warning
                        : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("ServiceName", httpContext.RequestServices.GetRequiredService<ServiceInfo>().Name);
                diagnosticContext.Set("CorrelationId", httpContext.Response.Headers[CorrelationIdHeader].ToString());
                diagnosticContext.Set("UserId", httpContext.Request.Headers[ServiceAccessControl.UserIdHeader].ToString());
                diagnosticContext.Set("UserRole", httpContext.Request.Headers[ServiceAccessControl.UserRoleHeader].ToString());
                diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString());
            };
        });

        return app;
    }

    private static void ConfigureResource(ResourceBuilder resource, string serviceName, IConfiguration configuration)
    {
        resource
            .AddService(
                serviceName: serviceName,
                serviceVersion: configuration["Service:Version"] ?? "local",
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production"
            });
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        var forwarded = context.Request.Headers[CorrelationIdHeader].ToString();
        return string.IsNullOrWhiteSpace(forwarded)
            ? Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
            : forwarded;
    }

    private static bool IsNoisePath(PathString path)
    {
        return path.StartsWithSegments("/health") ||
               path.StartsWithSegments("/persistence/health") ||
               path.StartsWithSegments("/metrics");
    }
}
