using MongoDB.Driver;

namespace TrafficEvents.Infrastructure.Mongo;

/// <summary>Fictional reference data: 20 junctions J-1..J-20.</summary>
public static class JunctionSeed
{
    private static readonly (string Name, string District, int Limit)[] Data =
    [
        ("Harbour Rd / Mill St", "Docklands", 50),
        ("Ring Rd North / Elm Ave", "Northgate", 70),
        ("Station Sq", "Centre", 30),
        ("Bridge St / River Walk", "Centre", 30),
        ("Ring Rd East / Canal St", "Eastfield", 70),
        ("Market Pl / King St", "Centre", 30),
        ("Airport Link / Ring Rd", "Westpark", 80),
        ("Hospital Rd / Oak Ln", "Northgate", 50),
        ("University Ave / Park Rd", "Southbank", 50),
        ("Industrial Way / Rail Xing", "Docklands", 50),
        ("Ring Rd South / Hill St", "Southbank", 70),
        ("Cathedral St / North Rd", "Centre", 30),
        ("Stadium Way / Lake Rd", "Eastfield", 50),
        ("Old Town Gate", "Old Town", 30),
        ("Ring Rd West / Mill Ln", "Westpark", 70),
        ("Ferry Terminal Approach", "Docklands", 50),
        ("Tech Park Rd / Ring Rd", "Westpark", 60),
        ("Garden St / Rose Ave", "Southbank", 30),
        ("Northern Bypass / A1 Slip", "Northgate", 80),
        ("Central Bus Station", "Centre", 30),
    ];

    public static IReadOnlyList<JunctionDocument> Junctions { get; } =
        Data.Select((j, i) => new JunctionDocument
        {
            Id = $"J-{i + 1}",
            Name = j.Name,
            District = j.District,
            SpeedLimitKmh = j.Limit,
        }).ToList();

    /// <summary>Upserts every junction; safe to run on every start.</summary>
    public static async Task SeedAsync(TrafficCollections collections, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collections);
        var writes = Junctions.Select(j => new ReplaceOneModel<JunctionDocument>(
            Builders<JunctionDocument>.Filter.Eq(x => x.Id, j.Id), j) { IsUpsert = true });
        await collections.Junctions.BulkWriteAsync(writes, cancellationToken: cancellationToken);
    }
}
