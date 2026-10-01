using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.Processor;

namespace TrafficEvents.IntegrationTests;

/// <summary>End to end through real RabbitMQ and MongoDB: publish → consume → store → alert.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class ProcessorPipelineTests(InfrastructureFixture infra)
{
    [Fact]
    public async Task Same_event_delivered_twice_is_stored_once()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync();
        var e = ProcessorHarness.Measurement();

        await h.PublishAsync(e);
        await h.PublishAsync(e); // sensor resend: same eventId
        await h.WaitForOutcomesAsync(2);

        h.Handler.Outcomes.Should().Equal(nameof(HandleOutcome.Processed), nameof(HandleOutcome.Duplicate));
        (await h.Collections.Events.CountDocumentsAsync(x => x.Id == e.EventId)).Should().Be(1);
        (await h.Collections.ProcessedEvents.CountDocumentsAsync(x => x.Id == e.EventId)).Should().Be(1);
    }

    [Fact]
    public async Task Event_is_enriched_and_updates_junction_status()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync();
        var e = ProcessorHarness.Measurement(speed: 37.5, junction: "J-3");

        await h.PublishAsync(e);
        await h.WaitForOutcomesAsync(1);

        var stored = await h.Collections.Events.Find(x => x.Id == e.EventId).SingleAsync();
        stored.JunctionName.Should().Be("Station Sq");
        stored.District.Should().Be("Centre");

        var status = await h.Collections.JunctionStatus.Find(x => x.Id == "J-3").SingleAsync();
        status.AvgSpeedKmh.Should().Be(37.5);
        status.LastMeasurementAt.Should().BeCloseTo(e.Timestamp.UtcDateTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Poison_message_is_dead_lettered_after_max_retries()
    {
        await using var h = new ProcessorHarness(infra, _ => new AlwaysFails());
        await h.StartAsync();
        var e = ProcessorHarness.Measurement();

        await h.PublishAsync(e);
        await ProcessorHarness.WaitUntilAsync(async () => await h.MessageCountAsync(Topology.DeadLetterQueue) == 1, because: "message in DLQ");

        h.Handler.Calls.Should().Be(1 + ProcessorHarness.MaxRetries, "first attempt plus 3 delayed retries");
        var dead = (await h.DrainAsync(Topology.DeadLetterQueue)).Should().ContainSingle().Subject;
        dead.BasicProperties.MessageId.Should().Be(e.EventId.ToString());
        EventConsumer.GetRetryCount(dead.BasicProperties).Should().Be(ProcessorHarness.MaxRetries);
        Header(dead, Topology.ErrorHeader).Should().Contain("always fails");
        Header(dead, Topology.ErrorTypeHeader).Should().Be(nameof(InvalidOperationException));
        (await h.MessageCountAsync(Topology.EventsQueue)).Should().Be(0);
        (await h.MessageCountAsync(Topology.RetryQueue)).Should().Be(0);
    }

    [Fact]
    public async Task Transient_failure_is_retried_then_succeeds_without_dead_lettering()
    {
        await using var h = new ProcessorHarness(infra, sp => new FlakyHandler(sp.GetRequiredService<TrafficEventHandler>(), failures: 2));
        await h.StartAsync();
        var e = ProcessorHarness.Measurement();

        await h.PublishAsync(e);
        await h.WaitForOutcomesAsync(3);

        h.Handler.Outcomes.Should().Equal(nameof(TimeoutException), nameof(TimeoutException), nameof(HandleOutcome.Processed));
        (await h.Collections.Events.CountDocumentsAsync(x => x.Id == e.EventId)).Should().Be(1);
        (await h.MessageCountAsync(Topology.DeadLetterQueue)).Should().Be(0);
    }

    [Theory]
    [InlineData("this is not json", "Malformed JSON")]
    [InlineData("""{"eventId":"7d6c0f0e-6b7a-4f43-9d55-0c4c7c1b2a01","type":"teleport"}""", "Invalid event")]
    public async Task Malformed_or_invalid_message_goes_straight_to_the_dlq(string body, string expectedError)
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync();

        await h.PublishRawAsync(Encoding.UTF8.GetBytes(body));
        await ProcessorHarness.WaitUntilAsync(async () => await h.MessageCountAsync(Topology.DeadLetterQueue) == 1);

        h.Handler.Calls.Should().Be(1, "retrying cannot fix bad input");
        var dead = (await h.DrainAsync(Topology.DeadLetterQueue)).Single();
        Header(dead, Topology.ErrorHeader).Should().StartWith(expectedError);
        EventConsumer.GetRetryCount(dead.BasicProperties).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_junction_is_dead_lettered_without_retries()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync();

        await h.PublishAsync(ProcessorHarness.Measurement(junction: "J-999"));
        await ProcessorHarness.WaitUntilAsync(async () => await h.MessageCountAsync(Topology.DeadLetterQueue) == 1);

        h.Handler.Calls.Should().Be(1);
        Header((await h.DrainAsync(Topology.DeadLetterQueue)).Single(), Topology.ErrorHeader).Should().Contain("Unknown junction");
    }

    [Fact]
    public async Task Low_speed_produces_exactly_one_alert_even_when_redelivered()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync();
        var slow = ProcessorHarness.Measurement(speed: 8);

        await h.PublishAsync(slow);
        await h.PublishAsync(slow);
        await h.WaitForOutcomesAsync(2);

        var alerts = await h.DrainAsync(Topology.AlertsQueue);
        var alert = alerts.Should().ContainSingle().Subject;
        alert.RoutingKey.Should().Be("alert.low_speed");
        var payload = JsonSerializer.Deserialize<IncidentAlert>(alert.Body.Span, TrafficJson.Options)!;
        payload.AlertId.Should().Be(slow.EventId);
        payload.Reason.Should().Be(AlertReasons.LowSpeed);
        payload.JunctionName.Should().Be("Harbour Rd / Mill St");
    }

    [Fact]
    public async Task Incident_produces_one_alert_and_normal_traffic_produces_none()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync();

        await h.PublishAsync(ProcessorHarness.Measurement(speed: 45), ProcessorHarness.Incident());
        await h.WaitForOutcomesAsync(2);

        var alert = (await h.DrainAsync(Topology.AlertsQueue)).Should().ContainSingle().Subject;
        alert.RoutingKey.Should().Be("alert.incident");
    }

    [Fact]
    public async Task Alert_not_published_before_a_crash_is_published_on_redelivery()
    {
        // Simulate "stored the alert record, then died before publishing": the gate document exists
        // but Published is false. The redelivered message must still produce the alert.
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync();
        var slow = ProcessorHarness.Measurement(speed: 5);
        await h.Collections.Alerts.InsertOneAsync(new()
        {
            Id = slow.EventId, JunctionId = slow.JunctionId, Reason = AlertReasons.LowSpeed, RaisedAt = DateTime.UtcNow, Published = false,
        });

        await h.PublishAsync(slow);
        await h.WaitForOutcomesAsync(1);

        (await h.DrainAsync(Topology.AlertsQueue)).Should().ContainSingle();
        (await h.Collections.Alerts.Find(a => a.Id == slow.EventId).SingleAsync()).Published.Should().BeTrue();
    }

    [Fact]
    public async Task Graceful_shutdown_finishes_the_in_flight_message_and_acks_it()
    {
        var slow = new SlowHandler(TimeSpan.FromSeconds(1.5));
        await using var h = new ProcessorHarness(infra, _ => slow);
        await h.StartAsync();

        await h.PublishAsync(ProcessorHarness.Measurement());
        await ProcessorHarness.WaitUntilAsync(() => Task.FromResult(slow.Started), because: "handler started");
        await h.Consumer.StopAsync(CancellationToken.None); // what SIGTERM triggers

        slow.Completed.Should().BeTrue("stop waits for in-flight work");
        (await h.MessageCountAsync(Topology.EventsQueue)).Should().Be(0, "the message was acked, not returned to the queue");
    }

    [Fact]
    public async Task Pod_killed_mid_processing_redelivers_and_the_retry_is_harmless()
    {
        // A hard kill means no ack: closing the channel without draining is the same thing from the
        // broker's point of view. The message goes back to the queue and the next consumer finishes it.
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);
        var e = ProcessorHarness.Measurement();
        await h.PublishAsync(e);

        // Do half the work by hand (event stored, not marked processed), then "die".
        var store = h.Get<EventStore>();
        var junction = (await h.Collections.Junctions.Find(j => j.Id == e.JunctionId).SingleAsync());
        await store.SaveEventAsync(e, junction, DateTime.UtcNow, CancellationToken.None);

        await h.Consumer.StartAsync(CancellationToken.None);
        await h.WaitForOutcomesAsync(1);

        h.Handler.Outcomes.Should().Equal(nameof(HandleOutcome.Processed));
        (await h.Collections.Events.CountDocumentsAsync(x => x.Id == e.EventId)).Should().Be(1);
        (await h.Collections.ProcessedEvents.CountDocumentsAsync(x => x.Id == e.EventId)).Should().Be(1);
    }

    private static string Header(RabbitMQ.Client.BasicGetResult message, string name) =>
        message.BasicProperties.Headers![name] switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            var other => other?.ToString() ?? string.Empty,
        };

    private sealed class AlwaysFails : IMessageHandler
    {
        public Task<HandleOutcome> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Handler always fails");
    }

    private sealed class SlowHandler(TimeSpan delay) : IMessageHandler
    {
        public bool Started { get; private set; }
        public bool Completed { get; private set; }

        public async Task<HandleOutcome> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            Started = true;
            await Task.Delay(delay, cancellationToken);
            Completed = true;
            return HandleOutcome.Processed;
        }
    }
}
