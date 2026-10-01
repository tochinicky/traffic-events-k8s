namespace TrafficEvents.Core;

/// <summary>
/// The event exactly as a sensor sends it. Every field is nullable so that the validator,
/// not the JSON deserializer, decides what is missing and can report it clearly.
/// </summary>
public sealed record TrafficEventRequest
{
    public Guid? EventId { get; init; }
    public string? SensorId { get; init; }
    public string? JunctionId { get; init; }
    public string? Type { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public int? VehicleCount { get; init; }
    public double? AvgSpeedKmh { get; init; }
    public IncidentRequest? Incident { get; init; }
}

public sealed record IncidentRequest
{
    public string? Kind { get; init; }
    public int? Lane { get; init; }
}
