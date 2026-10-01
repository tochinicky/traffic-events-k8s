using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace TrafficEvents.Infrastructure.Hosting;

/// <summary>What every service gets: JSON logs, health endpoints, /metrics, and a version endpoint.</summary>
public static class ServiceDefaults
{
    public static string Version { get; } =
        Environment.GetEnvironmentVariable("APP_VERSION")
        ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";

    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // One JSON object per line on stdout: kubectl logs / any log shipper can parse it.
        builder.Services.AddSerilog(log => log
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Extensions.Diagnostics.HealthChecks", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .Enrich.WithProperty("version", Version)
            .WriteTo.Console(new RenderedCompactJsonFormatter()));

        builder.Services.AddHealthChecks();

        // Kubernetes sends SIGTERM, waits terminationGracePeriodSeconds (30s), then SIGKILL.
        // Give hosted services most of that window to drain.
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

        return builder;
    }

    public static WebApplication MapServiceDefaults(this WebApplication app, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(o =>
        {
            // Probes and scrapes hit every few seconds (and a 503 from /health/ready is an expected
            // state, already logged by the failing health check); keep them out of the logs.
            o.GetLevel = (ctx, _, ex) =>
                IsInfraPath(ctx.Request.Path) ? LogEventLevel.Verbose
                : ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
                : LogEventLevel.Information;
        });
        app.UseHttpMetrics();

        // Liveness: "is the process alive and serving HTTP?" — no dependency checks, or a RabbitMQ
        // outage would make Kubernetes restart every pod, which fixes nothing.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        // Readiness: "should this pod receive work?" — dependencies must be reachable.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(HealthTags.Ready) });
        app.MapMetrics("/metrics");
        app.MapGet("/", () => Results.Ok(new { service = serviceName, version = Version }));

        return app;
    }

    private static bool IsInfraPath(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.Ordinal) || path.StartsWithSegments("/metrics", StringComparison.Ordinal);
}
