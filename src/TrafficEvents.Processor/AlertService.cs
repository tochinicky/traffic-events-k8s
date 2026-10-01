using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Prometheus;
using RabbitMQ.Client;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.Infrastructure.Mongo;

namespace TrafficEvents.Processor;

/// <summary>
/// Raises an incident alert at most once per event under redelivery. The <c>alerts</c> document
/// (keyed by event id) is the gate: once it is marked published, redeliveries skip it. If we crash
/// between publishing and marking, the redelivery publishes again with the same AlertId, so
/// alerts are at-least-once and consumers can de-duplicate on AlertId.
/// </summary>
public sealed class AlertService(
    TrafficCollections collections,
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    TimeProvider time) : IAsyncDisposable
{
    private static readonly Counter Raised = Metrics.CreateCounter(
        "processor_alerts_published_total", "Incident alerts published.", "reason");

    private readonly ConfirmingPublisher _publisher = new(connection, options.Value.PublishTimeout);

    public async Task RaiseAsync(TrafficEvent e, JunctionDocument junction, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(junction);

        var now = time.GetUtcNow();
        try
        {
            await collections.Alerts.InsertOneAsync(new AlertDocument
            {
                Id = e.EventId,
                JunctionId = e.JunctionId,
                Reason = reason,
                RaisedAt = now.UtcDateTime,
                Published = false,
            }, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (MongoSchema.IsDuplicateKey(ex))
        {
            var existing = await collections.Alerts.Find(a => a.Id == e.EventId).FirstOrDefaultAsync(cancellationToken);
            if (existing?.Published == true)
            {
                return;
            }
        }

        var alert = new IncidentAlert
        {
            AlertId = e.EventId,
            EventId = e.EventId,
            JunctionId = e.JunctionId,
            JunctionName = junction.Name,
            Reason = reason,
            IncidentKind = e.Incident?.Kind,
            Lane = e.Incident?.Lane,
            AvgSpeedKmh = e.AvgSpeedKmh,
            OccurredAt = e.Timestamp,
            RaisedAt = now,
        };

        await _publisher.PublishAsync(
            [
                new OutgoingMessage(
                    Topology.AlertsExchange,
                    Topology.AlertRoutingKey(reason),
                    JsonSerializer.SerializeToUtf8Bytes(alert, TrafficJson.Options),
                    new BasicProperties
                    {
                        Persistent = true,
                        MessageId = alert.AlertId.ToString(),
                        ContentType = "application/json",
                        Type = "incident-alert",
                    }),
            ],
            cancellationToken);

        await collections.Alerts.UpdateOneAsync(
            a => a.Id == e.EventId,
            Builders<AlertDocument>.Update.Set(a => a.Published, true),
            cancellationToken: cancellationToken);
        Raised.WithLabels(reason).Inc();
    }

    public ValueTask DisposeAsync() => _publisher.DisposeAsync();
}
