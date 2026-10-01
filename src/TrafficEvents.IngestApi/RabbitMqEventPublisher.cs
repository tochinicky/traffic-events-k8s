using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Messaging;

namespace TrafficEvents.IngestApi;

public sealed class RabbitMqEventPublisher(RabbitMqConnection connection, IOptions<RabbitMqOptions> options)
    : IEventPublisher, IAsyncDisposable
{
    private readonly ConfirmingPublisher _publisher = new(connection, options.Value.PublishTimeout);

    public Task PublishAsync(IReadOnlyList<TrafficEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        var messages = events.Select(e => new OutgoingMessage(
            Topology.EventsExchange,
            Topology.EventRoutingKey(e.Type),
            JsonSerializer.SerializeToUtf8Bytes(e, TrafficJson.Options),
            new BasicProperties
            {
                Persistent = true, // written to disk, survives a broker restart
                MessageId = e.EventId.ToString(),
                ContentType = "application/json",
                Type = e.Type,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            })).ToList();

        return _publisher.PublishAsync(messages, cancellationToken);
    }

    public ValueTask DisposeAsync() => _publisher.DisposeAsync();
}
