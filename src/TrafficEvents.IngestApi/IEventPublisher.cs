using TrafficEvents.Core;

namespace TrafficEvents.IngestApi;

public interface IEventPublisher
{
    /// <summary>Returns once the broker has confirmed every event; throws PublishFailedException otherwise.</summary>
    Task PublishAsync(IReadOnlyList<TrafficEvent> events, CancellationToken cancellationToken);
}
