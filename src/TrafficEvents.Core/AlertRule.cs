namespace TrafficEvents.Core;

/// <summary>Decides whether an event should raise an incident alert.</summary>
public static class AlertRule
{
    /// <returns>The alert reason, or null if no alert is needed.</returns>
    public static string? Evaluate(TrafficEvent trafficEvent, double lowSpeedThresholdKmh)
    {
        ArgumentNullException.ThrowIfNull(trafficEvent);

        if (trafficEvent.IsIncident)
        {
            return AlertReasons.Incident;
        }

        if (trafficEvent.AvgSpeedKmh < lowSpeedThresholdKmh)
        {
            return AlertReasons.LowSpeed;
        }

        return null;
    }
}
