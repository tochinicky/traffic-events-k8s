using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Prometheus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TrafficEvents.Infrastructure.Messaging;

namespace TrafficEvents.Processor;

/// <summary>
/// The RabbitMQ side of the processor: consume with manual acks, retry with a delay, dead-letter.
///
/// For every delivery exactly one of these happens, and only after the handler has finished:
///   success / duplicate          → ack
///   transient failure, retries left → publish a copy to the retry queue (retry-count + 1), then ack
///   retries exhausted / poison   → publish a copy to the DLX with the error, then ack
/// If the process dies before the ack, the broker redelivers the message — so the handler must be idempotent.
/// </summary>
public sealed class EventConsumer(
    RabbitMqConnection connection,
    IMessageHandler handler,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<EventConsumer> logger) : IAsyncDisposable
{
    private static readonly Counter Messages = Metrics.CreateCounter(
        "processor_messages_total", "Messages handled, by outcome.", "outcome");
    private static readonly Histogram Duration = Metrics.CreateHistogram(
        "processor_message_duration_seconds", "Time to handle one message.");

    private readonly RabbitMqOptions _options = options.Value;
    // Cancelled only if a graceful drain times out; normal shutdown lets in-flight work finish.
    private readonly CancellationTokenSource _abortProcessing = new();
    private IChannel? _channel;
    private string? _consumerTag;
    private int _inFlight;
    private volatile bool _stopping;
    private TaskCompletionSource? _drained;

    public bool IsConsuming => !_stopping && _consumerTag is not null && _channel is { IsOpen: true };

    public bool IsStopping => _stopping;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var conn = await connection.GetConnectionAsync(cancellationToken);

        // Confirms on this channel too: a retry/DLQ copy must be safely stored before we ack the original.
        var channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        // Prefetch bounds how many unacked messages this consumer holds; the rest stay in the queue
        // for other replicas (that is what lets the HPA spread load).
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _options.Prefetch, global: false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery);

        _channel = channel;
        _consumerTag = await channel.BasicConsumeAsync(Topology.EventsQueue, autoAck: false, consumer, cancellationToken);
        logger.LogInformation("Consuming {Queue} with prefetch {Prefetch}", Topology.EventsQueue, _options.Prefetch);
    }

    /// <summary>Restart after a channel-level failure (connection-level failures are recovered by the client).</summary>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await CloseChannelAsync();
        await StartAsync(cancellationToken);
    }

    /// <summary>
    /// Graceful shutdown: stop new deliveries, let the in-flight message finish and be acked, then
    /// close the channel. Prefetched-but-unstarted messages are returned to the queue by the broker.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopping = true;

        if (_channel is { IsOpen: true } channel && _consumerTag is not null)
        {
            try
            {
                await channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not cancel consumer cleanly");
            }
        }

        if (Volatile.Read(ref _inFlight) > 0)
        {
            logger.LogInformation("Waiting for {Count} in-flight message(s) to finish", Volatile.Read(ref _inFlight));
            try
            {
                await _drained.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Drain timed out; aborting in-flight work (it will be redelivered)");
                await _abortProcessing.CancelAsync();
            }
        }

        await CloseChannelAsync();
        logger.LogInformation("Consumer stopped");
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        if (_stopping)
        {
            // Leave it unacked: the broker requeues it when we close the channel.
            return;
        }

        Interlocked.Increment(ref _inFlight);
        var started = Stopwatch.GetTimestamp();
        var messageId = delivery.BasicProperties.MessageId;
        try
        {
            var retryCount = GetRetryCount(delivery.BasicProperties);
            try
            {
                var outcome = await handler.HandleAsync(delivery.Body, _abortProcessing.Token);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
                Messages.WithLabels(outcome == HandleOutcome.Duplicate ? "duplicate" : "processed").Inc();
                if (outcome == HandleOutcome.Duplicate)
                {
                    logger.LogInformation("Skipped duplicate event {EventId}", messageId);
                }
            }
            catch (PermanentMessageException ex)
            {
                logger.LogWarning("Dead-lettering poison message {EventId}: {Error}", messageId, ex.Message);
                await DeadLetterAsync(channel, delivery, retryCount, ex);
            }
            catch (Exception ex) when (retryCount >= _options.MaxRetries)
            {
                logger.LogError(ex, "Dead-lettering {EventId} after {Retries} retries", messageId, retryCount);
                await DeadLetterAsync(channel, delivery, retryCount, ex);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Retry {Attempt}/{Max} for {EventId} in {Delay}", retryCount + 1, _options.MaxRetries, messageId, _options.RetryDelay);
                await RetryLaterAsync(channel, delivery, retryCount + 1);
            }
        }
        catch (Exception ex)
        {
            // Ack/publish itself failed (channel or connection gone). The message is still unacked,
            // so the broker will redeliver it; idempotency makes that safe.
            logger.LogError(ex, "Could not settle {EventId}; it will be redelivered", messageId);
            await TryNackRequeueAsync(channel, delivery.DeliveryTag);
        }
        finally
        {
            Duration.Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
            if (Interlocked.Decrement(ref _inFlight) == 0 && _stopping)
            {
                _drained?.TrySetResult();
            }
        }
    }

    private async Task RetryLaterAsync(IChannel channel, BasicDeliverEventArgs delivery, int retryCount)
    {
        var props = CopyProperties(delivery.BasicProperties);
        props.Headers![Topology.RetryCountHeader] = retryCount;
        // Per-message TTL: sits in the retry queue for RetryDelay, then the broker routes it back.
        props.Expiration = ((long)_options.RetryDelay.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

        await channel.BasicPublishAsync(string.Empty, Topology.RetryQueue, mandatory: true, props, delivery.Body);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        Messages.WithLabels("retried").Inc();
    }

    private async Task DeadLetterAsync(IChannel channel, BasicDeliverEventArgs delivery, int retryCount, Exception error)
    {
        // Publishing to the DLX ourselves (instead of a plain reject) lets us attach the reason,
        // which is what an operator looking at the DLQ actually needs.
        var props = CopyProperties(delivery.BasicProperties);
        props.Headers![Topology.RetryCountHeader] = retryCount;
        props.Headers[Topology.ErrorHeader] = Truncate(error.Message, 500);
        props.Headers[Topology.ErrorTypeHeader] = error.GetType().Name;
        props.Headers[Topology.FailedAtHeader] = time.GetUtcNow().ToString("O");

        await channel.BasicPublishAsync(Topology.DeadLetterExchange, string.Empty, mandatory: true, props, delivery.Body);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        Messages.WithLabels("dead_lettered").Inc();
    }

    private static BasicProperties CopyProperties(IReadOnlyBasicProperties source)
    {
        var headers = new Dictionary<string, object?>();
        if (source.Headers is not null)
        {
            // Broker-owned x-* headers (e.g. x-death) are not ours to republish.
            foreach (var (key, value) in source.Headers.Where(h => !h.Key.StartsWith("x-", StringComparison.Ordinal)))
            {
                headers[key] = value;
            }
        }

        return new BasicProperties
        {
            Persistent = true,
            MessageId = source.MessageId,
            ContentType = source.ContentType,
            Type = source.Type,
            Timestamp = source.Timestamp,
            Headers = headers,
        };
    }

    public static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Headers is null || !properties.Headers.TryGetValue(Topology.RetryCountHeader, out var value))
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0,
        };
    }

    private async Task TryNackRequeueAsync(IChannel channel, ulong deliveryTag)
    {
        try
        {
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Nack failed; channel already closed");
        }
    }

    private async Task CloseChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        _consumerTag = null;
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.CloseAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Channel already closed");
        }

        channel.Dispose();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    public async ValueTask DisposeAsync()
    {
        await CloseChannelAsync();
        _abortProcessing.Dispose();
    }
}
