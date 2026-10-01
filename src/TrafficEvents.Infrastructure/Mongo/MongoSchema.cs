using MongoDB.Driver;

namespace TrafficEvents.Infrastructure.Mongo;

/// <summary>
/// Indexes, created idempotently by the processor at start-up (it owns the write model).
/// Every index here exists to serve a specific query; see the README for the mapping.
/// </summary>
public static class MongoSchema
{
    public static readonly TimeSpan EventRetention = TimeSpan.FromDays(7);

    public const string EventsByJunctionAndTime = "junctionId_1_timestamp_-1";
    public const string EventsByTypeAndTime = "type_1_timestamp_-1";
    public const string EventsTtl = "processedAt_ttl";
    public const string ProcessedEventsTtl = "processedAt_ttl";
    public const string AlertsTtl = "raisedAt_ttl";

    public static async Task EnsureIndexesAsync(TrafficCollections collections, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collections);

        await collections.Events.Indexes.CreateManyAsync(
            [
                // GET /junctions/{id}/speed: equality on junction, range on time.
                new CreateIndexModel<EventDocument>(
                    Builders<EventDocument>.IndexKeys.Ascending(e => e.JunctionId).Descending(e => e.Timestamp),
                    new CreateIndexOptions { Name = EventsByJunctionAndTime }),
                // GET /incidents?since=: equality on type, range + sort on time.
                new CreateIndexModel<EventDocument>(
                    Builders<EventDocument>.IndexKeys.Ascending(e => e.Type).Descending(e => e.Timestamp),
                    new CreateIndexOptions { Name = EventsByTypeAndTime }),
                // Raw events are kept 7 days. A background job in mongod deletes expired documents (~every 60s).
                new CreateIndexModel<EventDocument>(
                    Builders<EventDocument>.IndexKeys.Ascending(e => e.ProcessedAt),
                    new CreateIndexOptions { Name = EventsTtl, ExpireAfter = EventRetention }),
            ],
            cancellationToken);

        // Idempotency records only need to outlive any realistic redelivery window.
        await collections.ProcessedEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<ProcessedEventDocument>(
                Builders<ProcessedEventDocument>.IndexKeys.Ascending(p => p.ProcessedAt),
                new CreateIndexOptions { Name = ProcessedEventsTtl, ExpireAfter = EventRetention }),
            cancellationToken: cancellationToken);

        await collections.Alerts.Indexes.CreateOneAsync(
            new CreateIndexModel<AlertDocument>(
                Builders<AlertDocument>.IndexKeys.Ascending(a => a.RaisedAt),
                new CreateIndexOptions { Name = AlertsTtl, ExpireAfter = EventRetention }),
            cancellationToken: cancellationToken);
    }

    public static bool IsDuplicateKey(MongoWriteException ex) =>
        ex?.WriteError?.Category == ServerErrorCategory.DuplicateKey;
}
