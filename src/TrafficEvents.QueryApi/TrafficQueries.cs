using MongoDB.Driver;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Mongo;

namespace TrafficEvents.QueryApi;

public sealed record JunctionStatusResponse(
    string JunctionId,
    string? Name,
    string? District,
    int? SpeedLimitKmh,
    int? VehicleCount,
    double? AvgSpeedKmh,
    DateTime? LastMeasurementAt,
    LastIncident? LastIncident,
    DateTime? UpdatedAt);

public sealed record IncidentResponse(Guid EventId, string JunctionId, string JunctionName, string Kind, int Lane, DateTime Timestamp);

public sealed record SpeedResponse(string JunctionId, int Minutes, DateTime From, DateTime To, int Samples, double? AvgSpeedKmh, double? MinSpeedKmh, double? MaxSpeedKmh, int TotalVehicles);

/// <summary>Read side. Each query is backed by an index created in <see cref="MongoSchema"/>.</summary>
public sealed class TrafficQueries(TrafficCollections collections)
{
    /// <summary>Point lookup on _id.</summary>
    public async Task<JunctionStatusResponse?> GetStatusAsync(string junctionId, CancellationToken cancellationToken)
    {
        var s = await collections.JunctionStatus.Find(x => x.Id == junctionId).FirstOrDefaultAsync(cancellationToken);
        return s is null
            ? null
            : new JunctionStatusResponse(s.Id, s.JunctionName, s.District, s.SpeedLimitKmh, s.VehicleCount, s.AvgSpeedKmh, s.LastMeasurementAt, s.LastIncident, s.UpdatedAt);
    }

    /// <summary>Uses index { type: 1, timestamp: -1 }.</summary>
    public async Task<IReadOnlyList<IncidentResponse>> GetIncidentsAsync(DateTime since, int limit, CancellationToken cancellationToken)
    {
        var docs = await collections.Events
            .Find(e => e.Type == EventTypes.Incident && e.Timestamp >= since)
            .SortByDescending(e => e.Timestamp)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return docs
            .Where(d => d.Incident is not null)
            .Select(d => new IncidentResponse(d.Id, d.JunctionId, d.JunctionName, d.Incident!.Kind, d.Incident.Lane, d.Timestamp))
            .ToList();
    }

    /// <summary>Server-side aggregation; uses index { junctionId: 1, timestamp: -1 }.</summary>
    public async Task<SpeedResponse> GetSpeedAsync(string junctionId, int minutes, DateTime now, CancellationToken cancellationToken)
    {
        var from = now.AddMinutes(-minutes);
        var result = await collections.Events.Aggregate()
            .Match(e => e.JunctionId == junctionId && e.Timestamp >= from && e.Timestamp <= now && e.Type == EventTypes.Measurement)
            .Group(
                e => e.JunctionId,
                g => new
                {
                    Samples = g.Count(),
                    Avg = g.Average(e => e.AvgSpeedKmh),
                    Min = g.Min(e => e.AvgSpeedKmh),
                    Max = g.Max(e => e.AvgSpeedKmh),
                    Vehicles = g.Sum(e => e.VehicleCount ?? 0),
                })
            .FirstOrDefaultAsync(cancellationToken);

        return new SpeedResponse(
            junctionId, minutes, from, now,
            result?.Samples ?? 0,
            result?.Avg is { } avg ? Math.Round(avg, 1) : null,
            result?.Min, result?.Max,
            result?.Vehicles ?? 0);
    }

    public async Task<bool> JunctionExistsAsync(string junctionId, CancellationToken cancellationToken) =>
        await collections.Junctions.Find(j => j.Id == junctionId).AnyAsync(cancellationToken);
}
