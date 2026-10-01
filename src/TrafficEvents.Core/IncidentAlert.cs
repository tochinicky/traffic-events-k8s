namespace TrafficEvents.Core;

/// <summary>
/// Published to the <c>traffic.alerts</c> exchange. <see cref="AlertId"/> equals the source
/// event's id, so downstream consumers can de-duplicate on it.
/// </summary>
public sealed record IncidentAlert
{
    public required Guid AlertId { get; init; }
    public required Guid EventId { get; init; }
    public required string JunctionId { get; init; }
    public string? JunctionName { get; init; }
    public required string Reason { get; init; }
    public string? IncidentKind { get; init; }
    public int? Lane { get; init; }
    public double? AvgSpeedKmh { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset RaisedAt { get; init; }
}
