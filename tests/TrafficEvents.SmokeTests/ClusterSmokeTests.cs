using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TrafficEvents.SmokeTests;

public sealed class SmokeFactAttribute : FactAttribute
{
    public SmokeFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SMOKE_INGEST_URL")))
        {
            Skip = "SMOKE_INGEST_URL not set; run `make smoke` against a deployed cluster.";
        }
    }
}

/// <summary>The one end-to-end check: POST an event to ingest-api, see it via query-api.</summary>
public class ClusterSmokeTests
{
    private static string Env(string name, string fallback = "") => Environment.GetEnvironmentVariable(name) ?? fallback;

    [SmokeFact]
    public async Task Posted_event_becomes_visible_through_query_api()
    {
        using var ingest = new HttpClient { BaseAddress = new Uri(Env("SMOKE_INGEST_URL")) };
        using var query = new HttpClient { BaseAddress = new Uri(Env("SMOKE_QUERY_URL", "http://localhost:30081")) };
        ingest.DefaultRequestHeaders.Add("X-Api-Key", Env("SMOKE_API_KEY"));

        // J-20 is reserved for smoke tests in the demo, so the latest status is ours.
        var timestamp = DateTimeOffset.UtcNow.AddSeconds(-1);
        var speed = Math.Round(30 + (Random.Shared.NextDouble() * 20), 1);
        var response = await ingest.PostAsJsonAsync("/events", new
        {
            eventId = Guid.NewGuid(),
            sensorId = "S-999",
            junctionId = "J-20",
            type = "measurement",
            timestamp,
            vehicleCount = 7,
            avgSpeedKmh = speed,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        var deadline = DateTime.UtcNow.AddSeconds(30);
        JsonElement status = default;
        while (DateTime.UtcNow < deadline)
        {
            var get = await query.GetAsync(new Uri("/junctions/J-20/status", UriKind.Relative));
            if (get.StatusCode == HttpStatusCode.OK)
            {
                status = await get.Content.ReadFromJsonAsync<JsonElement>();
                if (status.GetProperty("avgSpeedKmh").GetDouble() == speed)
                {
                    return;
                }
            }

            await Task.Delay(500);
        }

        Assert.Fail($"Event not visible via query-api within 30s. Last status: {status}");
    }
}
