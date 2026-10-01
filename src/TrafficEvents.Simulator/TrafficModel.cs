using System.Text.Json.Nodes;
using TrafficEvents.Core;

namespace TrafficEvents.Simulator;

/// <summary>
/// Plausible-looking traffic: each junction's speed drifts around a baseline, with occasional
/// congestion that pushes it below the alert threshold. Not a traffic model, just believable data.
/// </summary>
public sealed class TrafficModel(SimulatorOptions options)
{
    private readonly Random _random = options.Seed is { } seed ? new Random(seed) : new Random();
    private readonly double[] _speed = Enumerable.Range(0, options.Junctions).Select(_ => 45.0).ToArray();
    private readonly List<JsonObject> _recent = [];

    public JsonObject Next()
    {
        var roll = _random.NextDouble();
        if (roll < options.DuplicateRatio && _recent.Count > 0)
        {
            // Sensors resend: same eventId, same body.
            return (JsonObject)_recent[_random.Next(_recent.Count)].DeepClone();
        }

        var junctionIndex = _random.Next(options.Junctions);
        var junctionId = _random.NextDouble() < options.PoisonRatio ? "J-999" : $"J-{junctionIndex + 1}";
        var isIncident = _random.NextDouble() < options.IncidentRatio;

        // Random walk with a pull back to 45 km/h; 3% chance of a jam.
        var speed = _speed[junctionIndex] + ((_random.NextDouble() - 0.5) * 8) + ((45 - _speed[junctionIndex]) * 0.1);
        if (_random.NextDouble() < 0.03)
        {
            speed = 5 + (_random.NextDouble() * 12);
        }

        speed = Math.Clamp(speed, 2, 90);
        _speed[junctionIndex] = speed;

        var e = new JsonObject
        {
            ["eventId"] = Guid.NewGuid(),
            ["sensorId"] = $"S-{100 + junctionIndex}",
            ["junctionId"] = junctionId,
            ["type"] = isIncident ? EventTypes.Incident : EventTypes.Measurement,
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["vehicleCount"] = _random.Next(0, 60),
            ["avgSpeedKmh"] = Math.Round(speed, 1),
        };

        if (isIncident)
        {
            e["incident"] = new JsonObject
            {
                ["kind"] = IncidentKinds.All[_random.Next(IncidentKinds.All.Count)],
                ["lane"] = _random.Next(1, 4),
            };
        }

        _recent.Add(e);
        if (_recent.Count > 500)
        {
            _recent.RemoveAt(0);
        }

        return e;
    }

    /// <summary>One of several ways to be wrong, so the 400 path gets exercised.</summary>
    public JsonObject Malformed()
    {
        var e = Next();
        switch (_random.Next(4))
        {
            case 0: e.Remove("eventId"); break;
            case 1: e["type"] = "teleport"; break;
            case 2: e["timestamp"] = DateTimeOffset.UtcNow.AddHours(2); break;
            default: e["avgSpeedKmh"] = -10; break;
        }

        return e;
    }

    public bool ShouldMalform() => _random.NextDouble() < options.MalformedRatio;
}
