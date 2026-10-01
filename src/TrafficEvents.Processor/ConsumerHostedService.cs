using TrafficEvents.Infrastructure.Mongo;

namespace TrafficEvents.Processor;

/// <summary>
/// Owns the consumer's lifecycle: prepare MongoDB, connect (retrying until the broker is up),
/// keep the consumer alive, and drain it on SIGTERM.
/// </summary>
public sealed class ConsumerHostedService(
    EventConsumer consumer,
    TrafficCollections collections,
    ILogger<ConsumerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RetryUntilAsync("prepare MongoDB", async ct =>
        {
            await MongoSchema.EnsureIndexesAsync(collections, ct);
            await JunctionSeed.SeedAsync(collections, ct);
        }, stoppingToken);

        await RetryUntilAsync("start consuming", consumer.StartAsync, stoppingToken);

        // Connection drops are recovered by the client library. A channel closed by a protocol
        // error is not, so watch for that and reopen.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!consumer.IsConsuming && !consumer.IsStopping)
            {
                await RetryUntilAsync("restart consumer", consumer.RestartAsync, stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Shutdown requested; draining consumer");
        await consumer.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task RetryUntilAsync(string what, Func<CancellationToken, Task> action, CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                await action(stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not {What}; retrying in {Delay}", what, delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }
}
