using System.Text.Json;
using Microsoft.Extensions.Options;
using TrafficEvents.Core;

namespace TrafficEvents.Processor;

/// <summary>
/// validate → de-duplicate → enrich → store → update status → alert → mark processed.
/// The processed marker is written last: if anything before it fails or the pod dies, the message
/// is redelivered and every step re-runs harmlessly.
/// </summary>
public sealed class TrafficEventHandler(
    EventStore store,
    JunctionDirectory junctions,
    AlertService alerts,
    IOptions<ProcessorOptions> options,
    TimeProvider time) : IMessageHandler
{
    public async Task<HandleOutcome> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        TrafficEventRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<TrafficEventRequest>(body.Span, TrafficJson.Options);
        }
        catch (JsonException ex)
        {
            throw new PermanentMessageException($"Malformed JSON: {ex.Message}", ex);
        }

        var validation = EventValidator.Validate(request, time.GetUtcNow());
        if (!validation.IsValid)
        {
            throw new PermanentMessageException(
                "Invalid event: " + string.Join("; ", validation.Errors.Select(e => $"{e.Field}: {e.Message}")));
        }

        var trafficEvent = validation.Event!;

        // Fast path for the common duplicate (sensor resend, redelivery after a crash).
        if (await store.IsProcessedAsync(trafficEvent.EventId, cancellationToken))
        {
            return HandleOutcome.Duplicate;
        }

        var junction = await junctions.FindAsync(trafficEvent.JunctionId, cancellationToken)
            ?? throw new PermanentMessageException($"Unknown junction '{trafficEvent.JunctionId}'.");

        var now = time.GetUtcNow().UtcDateTime;
        await store.SaveEventAsync(trafficEvent, junction, now, cancellationToken);
        await store.UpdateJunctionStatusAsync(trafficEvent, junction, now, cancellationToken);

        var reason = AlertRule.Evaluate(trafficEvent, options.Value.LowSpeedThresholdKmh);
        if (reason is not null)
        {
            await alerts.RaiseAsync(trafficEvent, junction, reason, cancellationToken);
        }

        await store.MarkProcessedAsync(trafficEvent.EventId, now, cancellationToken);
        return HandleOutcome.Processed;
    }
}
