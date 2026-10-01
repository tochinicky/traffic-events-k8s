using RabbitMQ.Client;

namespace TrafficEvents.Infrastructure.Messaging;

/// <summary>
/// Every exchange and queue in the system, declared in one place. Declarations are idempotent,
/// so every service declares the topology on connect and start-up order does not matter.
/// <code>
///  ingest-api ──► [traffic.events] (topic) ──event.*──► traffic.events.q ──► processor
///                                                          ▲        │ fail (retry &lt; max)
///                       (per-message TTL expires) ─────────┘        ▼
///                                                    traffic.events.retry
///                                                                   │ fail (retries exhausted / poison)
///                         [traffic.events.dlx] (fanout) ◄───────────┘
///                                   └──► traffic.events.dlq
///  processor ──► [traffic.alerts] (topic) ──alert.#──► traffic.alerts.q
/// </code>
/// </summary>
public static class Topology
{
    public const string EventsExchange = "traffic.events";
    public const string EventsQueue = "traffic.events.q";
    public const string RetryQueue = "traffic.events.retry";
    public const string DeadLetterExchange = "traffic.events.dlx";
    public const string DeadLetterQueue = "traffic.events.dlq";
    public const string AlertsExchange = "traffic.alerts";
    public const string AlertsQueue = "traffic.alerts.q";

    /// <summary>Our own header: how many times this message has been sent to the retry queue.</summary>
    public const string RetryCountHeader = "retry-count";
    public const string ErrorHeader = "error";
    public const string ErrorTypeHeader = "error-type";
    public const string FailedAtHeader = "failed-at";

    /// <summary>The alerts queue is for inspection in the demo; cap it so it cannot grow forever.</summary>
    public const int AlertsQueueMaxLength = 10_000;

    public static string EventRoutingKey(string eventType) => $"event.{eventType}";

    public static string AlertRoutingKey(string reason) => $"alert.{reason}";

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // Dead-letter side first, because the main queue points at it.
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: string.Empty, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(EventsQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                // Safety net: anything rejected without requeue lands in the DLQ rather than vanishing.
                ["x-dead-letter-exchange"] = DeadLetterExchange,
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(EventsQueue, EventsExchange, routingKey: "event.*", cancellationToken: cancellationToken);

        // Retry queue has no consumer. Each message carries its own TTL (the retry delay); when it
        // expires the broker dead-letters it via the default exchange straight back to the main queue.
        await channel.QueueDeclareAsync(RetryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = EventsQueue,
            },
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(AlertsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(AlertsQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-max-length"] = AlertsQueueMaxLength,
                ["x-overflow"] = "drop-head",
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(AlertsQueue, AlertsExchange, routingKey: "alert.#", cancellationToken: cancellationToken);
    }
}
