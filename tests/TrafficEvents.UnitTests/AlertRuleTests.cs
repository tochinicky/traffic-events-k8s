using TrafficEvents.Core;

namespace TrafficEvents.UnitTests;

public class AlertRuleTests
{
    private const double Threshold = 20;

    private static TrafficEvent Event(string type = EventTypes.Measurement, double? speed = 45) => new()
    {
        EventId = Guid.NewGuid(),
        SensorId = "S-1",
        JunctionId = "J-1",
        Type = type,
        Timestamp = DateTimeOffset.UtcNow,
        VehicleCount = 10,
        AvgSpeedKmh = speed,
        Incident = type == EventTypes.Incident ? new IncidentDetails(IncidentKinds.Accident, 1) : null,
    };

    [Fact]
    public void Normal_traffic_raises_nothing() => AlertRule.Evaluate(Event(speed: 45), Threshold).Should().BeNull();

    [Fact]
    public void Speed_below_threshold_raises_low_speed() =>
        AlertRule.Evaluate(Event(speed: 19.9), Threshold).Should().Be(AlertReasons.LowSpeed);

    [Fact]
    public void Speed_exactly_at_threshold_raises_nothing() =>
        AlertRule.Evaluate(Event(speed: 20), Threshold).Should().BeNull();

    [Fact]
    public void Incident_always_raises_incident_even_when_slow()
    {
        AlertRule.Evaluate(Event(EventTypes.Incident, speed: null), Threshold).Should().Be(AlertReasons.Incident);
        AlertRule.Evaluate(Event(EventTypes.Incident, speed: 3), Threshold).Should().Be(AlertReasons.Incident);
    }

    [Fact]
    public void Wire_format_contains_only_event_fields()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Event(EventTypes.Incident), TrafficJson.Options);

        json.Should().NotContain("isIncident");
        json.Should().Contain("\"incident\":{\"kind\":\"accident\",\"lane\":1}");
    }
}
