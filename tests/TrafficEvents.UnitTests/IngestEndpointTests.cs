using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.IngestApi;

namespace TrafficEvents.UnitTests;

/// <summary>The HTTP contract of POST /events, with RabbitMQ replaced by a fake.</summary>
public sealed class IngestEndpointTests : IDisposable
{
    private const string ApiKey = "test-key";
    private readonly FakePublisher _publisher = new();
    private readonly WebApplicationFactory<IngestOptions> _factory;

    public IngestEndpointTests()
    {
        _factory = new WebApplicationFactory<IngestOptions>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Ingest:ApiKey", ApiKey);
            b.ConfigureServices(s => s.AddSingleton<IEventPublisher>(_publisher));
        });
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Client(string? key = ApiKey)
    {
        var client = _factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        }

        return client;
    }

    private static object Valid(Guid? id = null) => new
    {
        eventId = id ?? Guid.NewGuid(),
        sensorId = "S-104",
        junctionId = "J-17",
        type = "measurement",
        timestamp = DateTimeOffset.UtcNow.AddSeconds(-1),
        vehicleCount = 42,
        avgSpeedKmh = 31.5,
    };

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Missing_or_wrong_api_key_is_401(string? key)
    {
        var response = await Client(key).PostAsJsonAsync("/events", Valid());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_valid_event_is_published_and_accepted()
    {
        var id = Guid.NewGuid();

        var response = await Client().PostAsJsonAsync("/events", Valid(id));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<AcceptedResponse>())!.Accepted.Should().Be(1);
        _publisher.Published.Should().ContainSingle().Which.EventId.Should().Be(id);
    }

    [Fact]
    public async Task Batch_of_valid_events_is_published_together()
    {
        var response = await Client().PostAsJsonAsync("/events", Enumerable.Range(0, 100).Select(_ => Valid()));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _publisher.Published.Should().HaveCount(100);
        _publisher.Calls.Should().Be(1, "a batch is one publish with one round of confirms");
    }

    [Fact]
    public async Task Batch_with_one_invalid_event_is_rejected_whole_with_indexed_errors()
    {
        var bad = new { eventId = Guid.NewGuid(), sensorId = "S-1", junctionId = "J-1", type = "teleport", timestamp = DateTimeOffset.UtcNow };

        var response = await Client().PostAsJsonAsync("/events", new[] { Valid(), bad });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Equal("[1].type");
        _publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Oversized_batch_is_rejected()
    {
        var response = await Client().PostAsJsonAsync("/events", Enumerable.Range(0, 101).Select(_ => Valid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _publisher.Published.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"eventId\": \"not-a-guid\"}")]
    public async Task Malformed_or_empty_body_is_400(string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await Client().PostAsync(new Uri("/events", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Broker_unavailable_is_503_not_silent_drop()
    {
        _publisher.FailWith = new PublishFailedException("connection refused");

        var response = await Client().PostAsJsonAsync("/events", Valid());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_the_broker()
    {
        var response = await Client(null).GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class FakePublisher : IEventPublisher
    {
        public List<TrafficEvent> Published { get; } = [];
        public int Calls { get; private set; }
        public Exception? FailWith { get; set; }

        public Task PublishAsync(IReadOnlyList<TrafficEvent> events, CancellationToken cancellationToken)
        {
            Calls++;
            if (FailWith is not null)
            {
                throw FailWith;
            }

            Published.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
