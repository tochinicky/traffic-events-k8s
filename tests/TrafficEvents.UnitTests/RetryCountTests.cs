using System.Text;
using RabbitMQ.Client;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.Processor;

namespace TrafficEvents.UnitTests;

public class RetryCountTests
{
    private static BasicProperties With(object? value) => new()
    {
        Headers = new Dictionary<string, object?> { [Topology.RetryCountHeader] = value },
    };

    [Fact]
    public void Missing_header_means_first_attempt() => EventConsumer.GetRetryCount(new BasicProperties()).Should().Be(0);

    [Fact]
    public void Header_is_read_whatever_numeric_type_the_broker_returns()
    {
        EventConsumer.GetRetryCount(With(2)).Should().Be(2);
        EventConsumer.GetRetryCount(With(3L)).Should().Be(3);
        EventConsumer.GetRetryCount(With(Encoding.UTF8.GetBytes("1"))).Should().Be(1);
    }
}
