namespace TrafficEvents.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    /// <summary>amqp://user:pass@host:5672/ — comes from a Kubernetes Secret.</summary>
    public string Uri { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>Max unacknowledged messages the broker pushes to one consumer.</summary>
    public ushort Prefetch { get; set; } = 20;

    /// <summary>Retries after the first failed attempt, before the message is dead-lettered.</summary>
    public int MaxRetries { get; set; } = 3;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a publish may wait for the broker's confirm before we give up (503).</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
