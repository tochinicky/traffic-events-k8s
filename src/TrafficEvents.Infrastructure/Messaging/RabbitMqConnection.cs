using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TrafficEvents.Infrastructure.Messaging;

/// <summary>
/// One long-lived AMQP connection per process (connections are expensive; channels are cheap).
/// Connects lazily so the app can start — and report "not ready" — while RabbitMQ is still down.
/// Once connected, the client's automatic recovery reconnects and re-declares topology.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen ?? false;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.Value.Uri),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                ClientProvidedName = $"{AppDomain.CurrentDomain.FriendlyName}@{Environment.MachineName}",
            };

            var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
            {
                await Topology.DeclareAsync(channel, cancellationToken);
            }

            logger.LogInformation("Connected to RabbitMQ at {Host}", factory.HostName);
            _connection = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _gate.Dispose();
    }
}
