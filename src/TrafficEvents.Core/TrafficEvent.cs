using System.Text.Json.Serialization;

namespace TrafficEvents.Core;

/// <summary>A validated traffic event. Only <see cref="EventValidator"/> creates these.</summary>
public sealed record TrafficEvent
{
    public required Guid EventId { get; init; }
    public required string SensorId { get; init; }
    public required string JunctionId { get; init; }
    public required string Type { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public int? VehicleCount { get; init; }
    public double? AvgSpeedKmh { get; init; }
    public IncidentDetails? Incident { get; init; }

    [JsonIgnore]
    public bool IsIncident => Type == EventTypes.Incident;
}

public sealed record IncidentDetails(string Kind, int Lane);
