using MongoDB.Bson.Serialization.Attributes;

namespace TrafficEvents.Infrastructure.Mongo;

// Stored shapes. Field names are camelCase (see MongoConventions). Dates are UTC DateTime because
// that is MongoDB's native date type (DateTimeOffset would serialise as an array).

/// <summary><c>junctions</c>: reference data used to enrich events. Seeded by the processor.</summary>
public sealed record JunctionDocument
{
    [BsonId] public required string Id { get; init; }
    public required string Name { get; init; }
    public required string District { get; init; }
    public required int SpeedLimitKmh { get; init; }
}

/// <summary><c>events</c>: every accepted event, enriched. Expires after 7 days (TTL on processedAt).</summary>
public sealed record EventDocument
{
    [BsonId] public required Guid Id { get; init; }
    public required string SensorId { get; init; }
    public required string JunctionId { get; init; }
    public required string JunctionName { get; init; }
    public required string District { get; init; }
    public required string Type { get; init; }
    public required DateTime Timestamp { get; init; }
    public int? VehicleCount { get; init; }
    public double? AvgSpeedKmh { get; init; }
    public IncidentInfo? Incident { get; init; }
    public required DateTime ProcessedAt { get; init; }
}

public sealed record IncidentInfo(string Kind, int Lane);

/// <summary><c>junction_status</c>: one document per junction, the latest known state.</summary>
public sealed record JunctionStatusDocument
{
    [BsonId] public required string Id { get; init; }
    public string? JunctionName { get; init; }
    public string? District { get; init; }
    public int? SpeedLimitKmh { get; init; }
    public int? VehicleCount { get; init; }
    public double? AvgSpeedKmh { get; init; }
    public DateTime? LastMeasurementAt { get; init; }
    public LastIncident? LastIncident { get; init; }
    public DateTime? LastIncidentAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record LastIncident(Guid EventId, string Kind, int Lane, DateTime At);

/// <summary><c>processed_events</c>: the idempotency record. <c>_id</c> is the event id, so it is unique by definition.</summary>
public sealed record ProcessedEventDocument
{
    [BsonId] public required Guid Id { get; init; }
    public required DateTime ProcessedAt { get; init; }
}

/// <summary><c>alerts</c>: one per event that raised an alert. Gates publishing so redeliveries do not re-alert.</summary>
public sealed record AlertDocument
{
    [BsonId] public required Guid Id { get; init; }
    public required string JunctionId { get; init; }
    public required string Reason { get; init; }
    public required DateTime RaisedAt { get; init; }
    public bool Published { get; init; }
}
