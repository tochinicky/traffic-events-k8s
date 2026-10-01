namespace TrafficEvents.Core;

/// <summary>String constants used on the wire and in MongoDB (lower snake case).</summary>
public static class EventTypes
{
    public const string Measurement = "measurement";
    public const string Incident = "incident";

    public static readonly IReadOnlyList<string> All = [Measurement, Incident];
}

public static class IncidentKinds
{
    public const string StalledVehicle = "stalled_vehicle";
    public const string Accident = "accident";
    public const string Roadworks = "roadworks";

    public static readonly IReadOnlyList<string> All = [StalledVehicle, Accident, Roadworks];
}

public static class AlertReasons
{
    public const string Incident = "incident";
    public const string LowSpeed = "low_speed";
}
