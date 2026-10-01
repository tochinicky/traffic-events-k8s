using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Prometheus;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Messaging;

namespace TrafficEvents.IngestApi;

public sealed record AcceptedResponse(int Accepted);

/// <summary>
/// POST /events — accepts a single event object or an array of up to 100. A batch is all-or-nothing:
/// if any event is invalid, nothing is published and every error is reported with its index.
/// </summary>
public static class IngestEndpoint
{
    private static readonly Counter Published = Metrics.CreateCounter(
        "ingest_events_published_total", "Events confirmed by RabbitMQ.", "type");
    private static readonly Counter Rejected = Metrics.CreateCounter(
        "ingest_events_rejected_total", "Requests rejected, by reason.", "reason");

    public static async Task<Results<Accepted<AcceptedResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        HttpRequest request,
        IEventPublisher publisher,
        IOptions<IngestOptions> options,
        TimeProvider time,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var logger = loggerFactory.CreateLogger(typeof(IngestEndpoint));

        List<TrafficEventRequest?> items;
        bool isBatch;
        try
        {
            using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            isBatch = doc.RootElement.ValueKind == JsonValueKind.Array;
            items = isBatch
                ? doc.RootElement.Deserialize<List<TrafficEventRequest?>>(TrafficJson.Options) ?? []
                : [doc.RootElement.Deserialize<TrafficEventRequest>(TrafficJson.Options)];
        }
        catch (JsonException ex)
        {
            Rejected.WithLabels("malformed_json").Inc();
            return Invalid("$", $"Malformed JSON: {ex.Message}");
        }

        if (items.Count == 0)
        {
            Rejected.WithLabels("empty_batch").Inc();
            return Invalid("$", "Batch must contain at least one event.");
        }

        if (items.Count > options.Value.MaxBatchSize)
        {
            Rejected.WithLabels("batch_too_large").Inc();
            return Invalid("$", $"Batch must contain at most {options.Value.MaxBatchSize} events.");
        }

        var now = time.GetUtcNow();
        var valid = new List<TrafficEvent>(items.Count);
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < items.Count; i++)
        {
            var result = EventValidator.Validate(items[i], now);
            if (result.IsValid)
            {
                valid.Add(result.Event!);
                continue;
            }

            foreach (var group in result.Errors.GroupBy(e => e.Field))
            {
                var key = isBatch ? $"[{i}].{group.Key}" : group.Key;
                errors[key] = group.Select(e => e.Message).ToArray();
            }
        }

        if (errors.Count > 0)
        {
            Rejected.WithLabels("validation").Inc();
            return TypedResults.ValidationProblem(errors, title: "One or more events are invalid.");
        }

        try
        {
            await publisher.PublishAsync(valid, cancellationToken);
        }
        catch (PublishFailedException ex)
        {
            // Never 202 something we could not hand to the broker: the sensor must retry.
            logger.LogError(ex, "Could not publish {Count} events", valid.Count);
            Rejected.WithLabels("broker_unavailable").Inc();
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Event broker unavailable; retry later.");
        }

        foreach (var group in valid.GroupBy(e => e.Type))
        {
            Published.WithLabels(group.Key).Inc(group.Count());
        }

        return TypedResults.Accepted((string?)null, new AcceptedResponse(valid.Count));
    }

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }, title: "Invalid request.");
}
