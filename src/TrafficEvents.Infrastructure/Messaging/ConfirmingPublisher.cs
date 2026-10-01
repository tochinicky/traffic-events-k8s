using RabbitMQ.Client;

namespace TrafficEvents.Infrastructure.Messaging;

public sealed record OutgoingMessage(string Exchange, string RoutingKey, ReadOnlyMemory<byte> Body, BasicProperties Properties);

public sealed class PublishFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Publishes with publisher confirms and <c>mandatory</c> on: a publish only succeeds once the
/// broker has taken responsibility for the message (persisted to a durable queue). A nack, an
/// unroutable message, a dead connection or a timeout all surface as <see cref="PublishFailedException"/>
/// so callers can fail loudly (503) instead of silently dropping events.
/// </summary>
public sealed class ConfirmingPublisher(RabbitMqConnection connection, TimeSpan timeout) : IAsyncDisposable
{
    // A channel must not be used by two publishers at once; batches are serialised through this gate.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(timeoutCts.Token);

            // Fire all publishes, then wait for every confirm: one round trip per batch, not per message.
            var confirms = messages
                .Select(m => channel.BasicPublishAsync(m.Exchange, m.RoutingKey, mandatory: true, m.Properties, m.Body, timeoutCts.Token).AsTask())
                .ToList();
            await Task.WhenAll(confirms);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The channel may be broken (closed, or confirms in an unknown state). Start fresh next time.
            await ResetChannelAsync();
            throw new PublishFailedException($"Publish to RabbitMQ failed: {ex.Message}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await ResetChannelAsync();
        var conn = await connection.GetConnectionAsync(cancellationToken);
        _channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        return _channel;
    }

    private async Task ResetChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception)
        {
            // Already broken; nothing useful to do.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}
