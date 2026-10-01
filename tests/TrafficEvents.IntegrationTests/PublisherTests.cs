using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.IngestApi;

namespace TrafficEvents.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class PublisherTests(InfrastructureFixture infra)
{
    [Fact]
    public async Task Confirmed_publish_lands_in_the_durable_queue_as_persistent()
    {
        await using var h = new ProcessorHarness(infra);
        await h.StartAsync(consume: false);
        var e = ProcessorHarness.Incident();

        await h.PublishAsync(e);

        var message = (await h.DrainAsync(Topology.EventsQueue)).Should().ContainSingle().Subject;
        message.RoutingKey.Should().Be("event.incident");
        message.BasicProperties.Persistent.Should().BeTrue();
        message.BasicProperties.MessageId.Should().Be(e.EventId.ToString());
    }

    [Fact]
    public async Task Publishing_when_the_broker_is_unreachable_fails_loudly()
    {
        var options = Options.Create(new RabbitMqOptions { Uri = "amqp://guest:guest@127.0.0.1:1/", PublishTimeout = TimeSpan.FromSeconds(3) });
        await using var connection = new RabbitMqConnection(options, NullLogger<RabbitMqConnection>.Instance);
        await using var publisher = new RabbitMqEventPublisher(connection, options);

        var act = () => publisher.PublishAsync([ProcessorHarness.Measurement()], CancellationToken.None);

        await act.Should().ThrowAsync<PublishFailedException>();
    }
}
