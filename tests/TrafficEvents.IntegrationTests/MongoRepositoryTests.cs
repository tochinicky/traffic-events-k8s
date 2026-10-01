using MongoDB.Bson;
using MongoDB.Driver;
using TrafficEvents.Infrastructure.Mongo;
using TrafficEvents.Processor;
using TrafficEvents.QueryApi;

namespace TrafficEvents.IntegrationTests;

/// <summary>Schema, write rules and read queries against a real MongoDB.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class MongoRepositoryTests(InfrastructureFixture infra)
{
    [Fact]
    public async Task Indexes_and_ttl_exist()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);

        var indexes = await (await h.Collections.Events.Indexes.ListAsync()).ToListAsync();
        var byName = indexes.ToDictionary(i => i["name"].AsString);

        byName.Keys.Should().Contain([MongoSchema.EventsByJunctionAndTime, MongoSchema.EventsByTypeAndTime, MongoSchema.EventsTtl]);
        byName[MongoSchema.EventsByJunctionAndTime]["key"].Should().Be(new BsonDocument { { "junctionId", 1 }, { "timestamp", -1 } });
        byName[MongoSchema.EventsTtl]["expireAfterSeconds"].ToInt64().Should().Be(7 * 24 * 3600);

        var processed = await (await h.Collections.ProcessedEvents.Indexes.ListAsync()).ToListAsync();
        processed.Should().Contain(i => i["name"] == MongoSchema.ProcessedEventsTtl);
    }

    [Fact]
    public async Task Speed_query_uses_the_junction_time_index()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);

        var explain = await h.Collections.Database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "find", TrafficCollections.EventsName },
                    { "filter", new BsonDocument { { "junctionId", "J-1" }, { "timestamp", new BsonDocument("$gte", DateTime.UtcNow.AddMinutes(-15)) } } },
                }
            },
        });

        explain.ToJson().Should().Contain(MongoSchema.EventsByJunctionAndTime);
    }

    [Fact]
    public async Task Older_event_does_not_overwrite_newer_junction_status()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);
        var store = h.Get<EventStore>();
        var junction = JunctionSeed.Junctions[0];
        var now = DateTimeOffset.UtcNow;

        await store.UpdateJunctionStatusAsync(ProcessorHarness.Measurement(speed: 50, at: now), junction, DateTime.UtcNow, default);
        await store.UpdateJunctionStatusAsync(ProcessorHarness.Measurement(speed: 10, at: now.AddMinutes(-1)), junction, DateTime.UtcNow, default);

        var status = await h.Collections.JunctionStatus.Find(s => s.Id == junction.Id).SingleAsync();
        status.AvgSpeedKmh.Should().Be(50, "the late, older reading must not win");
    }

    [Fact]
    public async Task Incident_and_measurement_update_status_independently()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);
        var store = h.Get<EventStore>();
        var junction = JunctionSeed.Junctions[0];
        var incident = ProcessorHarness.Incident();

        await store.UpdateJunctionStatusAsync(ProcessorHarness.Measurement(speed: 33), junction, DateTime.UtcNow, default);
        await store.UpdateJunctionStatusAsync(incident, junction, DateTime.UtcNow, default);

        var status = await h.Get<TrafficQueries>().GetStatusAsync(junction.Id, default);
        status!.AvgSpeedKmh.Should().Be(33);
        status.LastIncident!.EventId.Should().Be(incident.EventId);
        status.Name.Should().Be(junction.Name);
    }

    [Fact]
    public async Task Speed_aggregation_covers_only_the_window_and_measurements()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);
        var store = h.Get<EventStore>();
        var junction = JunctionSeed.Junctions[0];
        var now = DateTimeOffset.UtcNow;

        foreach (var (speed, minutesAgo) in new[] { (30.0, 1), (40.0, 5), (50.0, 14), (99.0, 30) })
        {
            await store.SaveEventAsync(ProcessorHarness.Measurement(speed, at: now.AddMinutes(-minutesAgo)), junction, DateTime.UtcNow, default);
        }

        await store.SaveEventAsync(ProcessorHarness.Measurement(10, junction: "J-2"), JunctionSeed.Junctions[1], DateTime.UtcNow, default);
        await store.SaveEventAsync(ProcessorHarness.Incident(), junction, DateTime.UtcNow, default);

        var speed15 = await h.Get<TrafficQueries>().GetSpeedAsync(junction.Id, 15, now.UtcDateTime, default);

        speed15.Samples.Should().Be(3);
        speed15.AvgSpeedKmh.Should().Be(40);
        speed15.MinSpeedKmh.Should().Be(30);
        speed15.MaxSpeedKmh.Should().Be(50);
        speed15.TotalVehicles.Should().Be(36);
    }

    [Fact]
    public async Task Speed_aggregation_with_no_data_returns_zero_samples()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);

        var result = await h.Get<TrafficQueries>().GetSpeedAsync("J-5", 15, DateTime.UtcNow, default);

        result.Samples.Should().Be(0);
        result.AvgSpeedKmh.Should().BeNull();
    }

    [Fact]
    public async Task Incidents_query_returns_recent_incidents_newest_first()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);
        var store = h.Get<EventStore>();
        var junction = JunctionSeed.Junctions[0];
        var now = DateTimeOffset.UtcNow;
        var older = ProcessorHarness.Incident(at: now.AddMinutes(-10));
        var newer = ProcessorHarness.Incident(at: now.AddMinutes(-2));
        var tooOld = ProcessorHarness.Incident(at: now.AddHours(-3));

        foreach (var e in new[] { older, newer, tooOld, ProcessorHarness.Measurement() })
        {
            await store.SaveEventAsync(e, junction, DateTime.UtcNow, default);
        }

        var incidents = await h.Get<TrafficQueries>().GetIncidentsAsync(now.UtcDateTime.AddHours(-1), 50, default);

        incidents.Select(i => i.EventId).Should().Equal(newer.EventId, older.EventId);
    }
}
