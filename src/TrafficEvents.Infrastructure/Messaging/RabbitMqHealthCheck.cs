using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TrafficEvents.Infrastructure.Messaging;

/// <summary>Readiness: can we reach RabbitMQ right now? (Connects on first call.)</summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var conn = await connection.GetConnectionAsync(cancellationToken);
            return conn.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed (recovering).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ unreachable.", ex);
        }
    }
}
