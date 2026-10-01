using TrafficEvents.Core;

namespace TrafficEvents.UnitTests;

public class EventValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 15, 0, TimeSpan.Zero);

    internal static TrafficEventRequest Measurement() => new()
    {
        EventId = Guid.NewGuid(),
        SensorId = "S-104",
        JunctionId = "J-17",
        Type = EventTypes.Measurement,
        Timestamp = Now.AddSeconds(-5),
        VehicleCount = 42,
        AvgSpeedKmh = 31.5,
    };

    private static TrafficEventRequest Incident() => Measurement() with
    {
        Type = EventTypes.Incident,
        VehicleCount = null,
        AvgSpeedKmh = null,
        Incident = new IncidentRequest { Kind = IncidentKinds.Accident, Lane = 2 },
    };

    private static IEnumerable<string> FieldsWithErrors(TrafficEventRequest? request) =>
        EventValidator.Validate(request, Now).Errors.Select(e => e.Field);

    [Fact]
    public void Valid_measurement_passes_and_is_normalised()
    {
        var request = Measurement() with { Timestamp = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2)) };

        var result = EventValidator.Validate(request, Now);

        result.IsValid.Should().BeTrue();
        result.Event!.Timestamp.Offset.Should().Be(TimeSpan.Zero, "timestamps are stored in UTC");
        result.Event.Incident.Should().BeNull();
    }

    [Fact]
    public void Valid_incident_passes_without_a_measurement()
    {
        var result = EventValidator.Validate(Incident(), Now);

        result.IsValid.Should().BeTrue();
        result.Event!.Incident.Should().Be(new IncidentDetails(IncidentKinds.Accident, 2));
    }

    [Fact]
    public void Null_body_is_rejected() => FieldsWithErrors(null).Should().Equal("$");

    [Fact]
    public void Missing_or_empty_event_id_is_rejected()
    {
        FieldsWithErrors(Measurement() with { EventId = null }).Should().Equal("eventId");
        FieldsWithErrors(Measurement() with { EventId = Guid.Empty }).Should().Equal("eventId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("104")]
    [InlineData("S-")]
    [InlineData("s-104")]
    public void Malformed_sensor_id_is_rejected(string? sensorId) =>
        FieldsWithErrors(Measurement() with { SensorId = sensorId }).Should().Equal("sensorId");

    [Theory]
    [InlineData(null)]
    [InlineData("17")]
    [InlineData("J-17; drop")]
    public void Malformed_junction_id_is_rejected(string? junctionId) =>
        FieldsWithErrors(Measurement() with { JunctionId = junctionId }).Should().Equal("junctionId");

    [Theory]
    [InlineData(null)]
    [InlineData("teleport")]
    [InlineData("Measurement")]
    public void Unknown_type_is_rejected(string? type)
    {
        var result = EventValidator.Validate(Measurement() with { Type = type }, Now);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("measurement, incident");
    }

    [Fact]
    public void Future_timestamp_is_rejected_beyond_clock_skew()
    {
        FieldsWithErrors(Measurement() with { Timestamp = Now.AddMinutes(5) }).Should().Equal("timestamp");
        FieldsWithErrors(Measurement() with { Timestamp = null }).Should().Equal("timestamp");
    }

    [Fact]
    public void Small_clock_skew_is_tolerated() =>
        EventValidator.Validate(Measurement() with { Timestamp = Now.AddSeconds(20) }, Now).IsValid.Should().BeTrue();

    [Fact]
    public void Measurement_requires_count_and_speed()
    {
        FieldsWithErrors(Measurement() with { VehicleCount = null, AvgSpeedKmh = null })
            .Should().BeEquivalentTo("vehicleCount", "avgSpeedKmh");
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(10_001, 30)]
    [InlineData(10, -0.1)]
    [InlineData(10, 300.1)]
    [InlineData(10, double.NaN)]
    public void Measurement_values_out_of_range_are_rejected(int count, double speed) =>
        FieldsWithErrors(Measurement() with { VehicleCount = count, AvgSpeedKmh = speed }).Should().NotBeEmpty();

    [Fact]
    public void Measurement_must_not_carry_an_incident() =>
        FieldsWithErrors(Measurement() with { Incident = new IncidentRequest { Kind = IncidentKinds.Accident, Lane = 1 } })
            .Should().Equal("incident");

    [Fact]
    public void Incident_requires_details() =>
        FieldsWithErrors(Incident() with { Incident = null }).Should().Equal("incident");

    [Theory]
    [InlineData("alien_landing", 1, "incident.kind")]
    [InlineData(null, 1, "incident.kind")]
    [InlineData(IncidentKinds.Roadworks, 0, "incident.lane")]
    [InlineData(IncidentKinds.Roadworks, 13, "incident.lane")]
    public void Incident_details_are_validated(string? kind, int lane, string field) =>
        FieldsWithErrors(Incident() with { Incident = new IncidentRequest { Kind = kind, Lane = lane } }).Should().Equal(field);

    [Fact]
    public void All_errors_are_reported_together()
    {
        var result = EventValidator.Validate(new TrafficEventRequest(), Now);

        result.Errors.Select(e => e.Field).Should().BeEquivalentTo("eventId", "sensorId", "junctionId", "timestamp", "type");
    }
}
