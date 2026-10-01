using System.Text.RegularExpressions;

namespace TrafficEvents.Core;

public sealed record ValidationError(string Field, string Message);

public sealed record ValidationResult(TrafficEvent? Event, IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates incoming events. Used by ingest-api (reject with 400) and again by the
/// processor, which must not trust whatever happens to be on the queue.
/// </summary>
public static partial class EventValidator
{
    /// <summary>Sensor clocks drift; allow a little slack before calling a timestamp "future".</summary>
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(30);

    public const int MaxVehicleCount = 10_000;
    public const double MaxSpeedKmh = 300;
    public const int MaxLane = 12;

    [GeneratedRegex(@"^S-\d{1,6}$")]
    private static partial Regex SensorIdPattern();

    [GeneratedRegex(@"^J-\d{1,6}$")]
    private static partial Regex JunctionIdPattern();

    public static ValidationResult Validate(TrafficEventRequest? request, DateTimeOffset now)
    {
        if (request is null)
        {
            return new ValidationResult(null, [new ValidationError("$", "Body must be a traffic event object.")]);
        }

        var errors = new List<ValidationError>();

        if (request.EventId is null || request.EventId == Guid.Empty)
        {
            errors.Add(new("eventId", "eventId is required and must be a non-empty UUID."));
        }

        if (string.IsNullOrWhiteSpace(request.SensorId) || !SensorIdPattern().IsMatch(request.SensorId))
        {
            errors.Add(new("sensorId", "sensorId is required and must look like 'S-104'."));
        }

        if (string.IsNullOrWhiteSpace(request.JunctionId) || !JunctionIdPattern().IsMatch(request.JunctionId))
        {
            errors.Add(new("junctionId", "junctionId is required and must look like 'J-17'."));
        }

        if (request.Timestamp is null)
        {
            errors.Add(new("timestamp", "timestamp is required (ISO 8601, UTC)."));
        }
        else if (request.Timestamp > now + AllowedClockSkew)
        {
            errors.Add(new("timestamp", "timestamp must not be in the future."));
        }

        switch (request.Type)
        {
            case EventTypes.Measurement:
                ValidateMeasurement(request, errors);
                break;
            case EventTypes.Incident:
                ValidateIncident(request, errors);
                break;
            default:
                errors.Add(new("type", $"type is required and must be one of: {string.Join(", ", EventTypes.All)}."));
                break;
        }

        if (errors.Count > 0)
        {
            return new ValidationResult(null, errors);
        }

        var valid = new TrafficEvent
        {
            EventId = request.EventId!.Value,
            SensorId = request.SensorId!,
            JunctionId = request.JunctionId!,
            Type = request.Type!,
            Timestamp = request.Timestamp!.Value.ToUniversalTime(),
            VehicleCount = request.VehicleCount,
            AvgSpeedKmh = request.AvgSpeedKmh,
            Incident = request.Incident is { Kind: { } kind, Lane: { } lane } ? new IncidentDetails(kind, lane) : null,
        };
        return new ValidationResult(valid, []);
    }

    private static void ValidateMeasurement(TrafficEventRequest request, List<ValidationError> errors)
    {
        if (request.VehicleCount is null or < 0 or > MaxVehicleCount)
        {
            errors.Add(new("vehicleCount", $"vehicleCount is required for measurements and must be 0-{MaxVehicleCount}."));
        }

        ValidateSpeed(request, errors, required: true);

        if (request.Incident is not null)
        {
            errors.Add(new("incident", "incident must be omitted for measurement events."));
        }
    }

    private static void ValidateIncident(TrafficEventRequest request, List<ValidationError> errors)
    {
        if (request.Incident is null)
        {
            errors.Add(new("incident", "incident is required for incident events."));
        }
        else
        {
            if (request.Incident.Kind is null || !IncidentKinds.All.Contains(request.Incident.Kind))
            {
                errors.Add(new("incident.kind", $"incident.kind must be one of: {string.Join(", ", IncidentKinds.All)}."));
            }

            if (request.Incident.Lane is null or < 1 or > MaxLane)
            {
                errors.Add(new("incident.lane", $"incident.lane is required and must be 1-{MaxLane}."));
            }
        }

        // Incidents may carry a measurement too, but if present it must be sane.
        if (request.VehicleCount is < 0 or > MaxVehicleCount)
        {
            errors.Add(new("vehicleCount", $"vehicleCount must be 0-{MaxVehicleCount}."));
        }

        ValidateSpeed(request, errors, required: false);
    }

    private static void ValidateSpeed(TrafficEventRequest request, List<ValidationError> errors, bool required)
    {
        if (request.AvgSpeedKmh is null)
        {
            if (required)
            {
                errors.Add(new("avgSpeedKmh", "avgSpeedKmh is required for measurements."));
            }

            return;
        }

        if (double.IsNaN(request.AvgSpeedKmh.Value) || request.AvgSpeedKmh is < 0 or > MaxSpeedKmh)
        {
            errors.Add(new("avgSpeedKmh", $"avgSpeedKmh must be 0-{MaxSpeedKmh}."));
        }
    }
}
