using MongoDB.Driver;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Mongo;

namespace TrafficEvents.Processor;

/// <summary>
/// MongoDB writes for one event. Each write is idempotent on its own, so a redelivery that
/// re-runs some or all of them leaves the same end state.
/// </summary>
public sealed class EventStore(TrafficCollections collections)
{
    public async Task<bool> IsProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
        await collections.ProcessedEvents.Find(p => p.Id == eventId).AnyAsync(cancellationToken);

    /// <summary>Recorded last, after all other writes, and only then do we ack.</summary>
    public Task MarkProcessedAsync(Guid eventId, DateTime now, CancellationToken cancellationToken) =>
        IgnoreDuplicateAsync(collections.ProcessedEvents.InsertOneAsync(
            new ProcessedEventDocument { Id = eventId, ProcessedAt = now }, cancellationToken: cancellationToken));

    public Task SaveEventAsync(TrafficEvent e, JunctionDocument junction, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(junction);

        var doc = new EventDocument
        {
            Id = e.EventId,
            SensorId = e.SensorId,
            JunctionId = e.JunctionId,
            JunctionName = junction.Name,
            District = junction.District,
            Type = e.Type,
            Timestamp = e.Timestamp.UtcDateTime,
            VehicleCount = e.VehicleCount,
            AvgSpeedKmh = e.AvgSpeedKmh,
            Incident = e.Incident is null ? null : new IncidentInfo(e.Incident.Kind, e.Incident.Lane),
            ProcessedAt = now,
        };

        // _id is the event id: a second insert of the same event is a duplicate-key no-op.
        return IgnoreDuplicateAsync(collections.Events.InsertOneAsync(doc, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Update the junction's latest state, but only if this event is newer than what we have:
    /// sensors resend and messages are retried, so events arrive out of order.
    /// </summary>
    public Task UpdateJunctionStatusAsync(TrafficEvent e, JunctionDocument junction, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(junction);

        var f = Builders<JunctionStatusDocument>.Filter;
        var u = Builders<JunctionStatusDocument>.Update;
        var at = e.Timestamp.UtcDateTime;

        FilterDefinition<JunctionStatusDocument> newerThanStored;
        UpdateDefinition<JunctionStatusDocument> update = u
            .Set(s => s.JunctionName, junction.Name)
            .Set(s => s.District, junction.District)
            .Set(s => s.SpeedLimitKmh, junction.SpeedLimitKmh)
            .Set(s => s.UpdatedAt, now);

        if (e.IsIncident)
        {
            newerThanStored = f.Or(f.Eq(s => s.LastIncidentAt, null), f.Lt(s => s.LastIncidentAt, at));
            update = update
                .Set(s => s.LastIncidentAt, at)
                .Set(s => s.LastIncident, new LastIncident(e.EventId, e.Incident!.Kind, e.Incident.Lane, at));
        }
        else
        {
            newerThanStored = f.Or(f.Eq(s => s.LastMeasurementAt, null), f.Lt(s => s.LastMeasurementAt, at));
            update = update
                .Set(s => s.LastMeasurementAt, at)
                .Set(s => s.VehicleCount, e.VehicleCount)
                .Set(s => s.AvgSpeedKmh, e.AvgSpeedKmh);
        }

        // If the stored state is newer, the filter misses, the upsert tries to insert the same _id,
        // and MongoDB reports a duplicate key: exactly the "stale event, ignore" case.
        return IgnoreDuplicateAsync(collections.JunctionStatus.UpdateOneAsync(
            f.And(f.Eq(s => s.Id, e.JunctionId), newerThanStored),
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken));
    }

    private static async Task IgnoreDuplicateAsync(Task write)
    {
        try
        {
            await write;
        }
        catch (MongoWriteException ex) when (MongoSchema.IsDuplicateKey(ex))
        {
            // Already there: idempotent no-op.
        }
    }
}
