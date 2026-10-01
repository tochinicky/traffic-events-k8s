namespace TrafficEvents.Processor;

public enum HandleOutcome
{
    Processed,
    Duplicate,
}

/// <summary>The business side of consuming: given a message body, do the work. Knows nothing about acks.</summary>
public interface IMessageHandler
{
    /// <exception cref="PermanentMessageException">The message can never succeed; dead-letter it now.</exception>
    /// <remarks>Any other exception is treated as transient and the message is retried.</remarks>
    Task<HandleOutcome> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken);
}

/// <summary>Malformed, invalid or unprocessable input. Retrying would not help.</summary>
public sealed class PermanentMessageException(string message, Exception? inner = null) : Exception(message, inner);
