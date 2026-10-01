using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TrafficEvents.Processor;

/// <summary>Readiness also requires that we are actually consuming, not just connected.</summary>
public sealed class ConsumerHealthCheck(EventConsumer consumer) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(consumer.IsConsuming
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Not consuming from RabbitMQ."));
}
