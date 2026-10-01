using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using TrafficEvents.Core;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.Infrastructure.Mongo;
using TrafficEvents.IngestApi;
using TrafficEvents.Processor;

namespace TrafficEvents.IntegrationTests;

/// <summary>
/// Wires the real processor (consumer + handler + Mongo + RabbitMQ) against the test containers,
/// with a fresh database per test and a short retry delay. The handler can be swapped out.
/// </summary>
public sealed class ProcessorHarness : IAsyncDisposable
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    public const int MaxRetries = 3;

    private readonly ServiceProvider _services;
    private IChannel? _admin;

    public ProcessorHarness(InfrastructureFixture infra, Func<IServiceProvider, IMessageHandler>? handler = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Mongo"] = infra.MongoDb.GetConnectionString(),
            ["Mongo:Database"] = $"test_{Guid.NewGuid():N}",
            ["RabbitMq:Uri"] = infra.RabbitMq.GetConnectionString(),
            ["RabbitMq:MaxRetries"] = MaxRetries.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:RetryDelay"] = RetryDelay.ToString(),
            ["Processor:LowSpeedThresholdKmh"] = "20",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProcessor(config);
        services.AddSingleton<TrafficEventHandler>();
        services.AddSingleton<IMessageHandler>(sp =>
            new RecordingHandler(handler?.Invoke(sp) ?? sp.GetRequiredService<TrafficEventHandler>()));
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.AddSingleton<TrafficEvents.QueryApi.TrafficQueries>();
        _services = services.BuildServiceProvider();
    }

    public TrafficCollections Collections => _services.GetRequiredService<TrafficCollections>();
    public EventConsumer Consumer => _services.GetRequiredService<EventConsumer>();
    public RecordingHandler Handler => (RecordingHandler)_services.GetRequiredService<IMessageHandler>();
    public IEventPublisher Publisher => _services.GetRequiredService<IEventPublisher>();
    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Purge queues, prepare Mongo, optionally start consuming.</summary>
    public async Task StartAsync(bool consume = true)
    {
        var connection = await _services.GetRequiredService<RabbitMqConnection>().GetConnectionAsync();
        _admin = await connection.CreateChannelAsync();
        foreach (var queue in new[] { Topology.EventsQueue, Topology.RetryQueue, Topology.DeadLetterQueue, Topology.AlertsQueue })
        {
            await _admin.QueuePurgeAsync(queue);
        }

        await MongoSchema.EnsureIndexesAsync(Collections);
        await JunctionSeed.SeedAsync(Collections);
        if (consume)
        {
            await Consumer.StartAsync(CancellationToken.None);
        }
    }

    public Task PublishAsync(params TrafficEvent[] events) => Publisher.PublishAsync(events, CancellationToken.None);

    public async Task PublishRawAsync(byte[] body) =>
        await _admin!.BasicPublishAsync(Topology.EventsExchange, Topology.EventRoutingKey(EventTypes.Measurement),
            mandatory: true, new BasicProperties { Persistent = true, MessageId = "raw" }, body);

    public async Task<uint> MessageCountAsync(string queue) => (await _admin!.QueueDeclarePassiveAsync(queue)).MessageCount;

    public async Task<List<BasicGetResult>> DrainAsync(string queue)
    {
        var messages = new List<BasicGetResult>();
        while (await _admin!.BasicGetAsync(queue, autoAck: true) is { } message)
        {
            messages.Add(message);
        }

        return messages;
    }

    public static TrafficEvent Measurement(double speed = 45, string junction = "J-1", DateTimeOffset? at = null) => new()
    {
        EventId = Guid.NewGuid(),
        SensorId = "S-1",
        JunctionId = junction,
        Type = EventTypes.Measurement,
        Timestamp = at ?? DateTimeOffset.UtcNow.AddSeconds(-1),
        VehicleCount = 12,
        AvgSpeedKmh = speed,
    };

    public static TrafficEvent Incident(string junction = "J-1", DateTimeOffset? at = null) => new()
    {
        EventId = Guid.NewGuid(),
        SensorId = "S-1",
        JunctionId = junction,
        Type = EventTypes.Incident,
        Timestamp = at ?? DateTimeOffset.UtcNow.AddSeconds(-1),
        Incident = new IncidentDetails(IncidentKinds.StalledVehicle, 2),
    };

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition not met in time{(because is null ? "" : $": {because}")}");
    }

    public Task WaitForOutcomesAsync(int count) =>
        WaitUntilAsync(() => Task.FromResult(Handler.Calls >= count), because: $"{count} handler calls (saw {Handler.Calls})");

    public async ValueTask DisposeAsync()
    {
        await Consumer.StopAsync(CancellationToken.None);
        if (_admin is not null)
        {
            await _admin.DisposeAsync();
        }

        var db = Collections.Database;
        await db.Client.DropDatabaseAsync(db.DatabaseNamespace.DatabaseName);
        await _services.DisposeAsync();
    }
}

/// <summary>Decorator that records each call and its result, so tests can wait on "processed N messages".</summary>
public sealed class RecordingHandler(IMessageHandler inner) : IMessageHandler
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);
    public ConcurrentQueue<string> Outcomes { get; } = new();

    public async Task<HandleOutcome> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await inner.HandleAsync(body, cancellationToken);
            Outcomes.Enqueue(outcome.ToString());
            return outcome;
        }
        catch (Exception ex)
        {
            Outcomes.Enqueue(ex.GetType().Name);
            throw;
        }
        finally
        {
            Interlocked.Increment(ref _calls);
        }
    }
}

/// <summary>Fails the first N calls with a transient error (as if MongoDB were briefly down), then delegates.</summary>
public sealed class FlakyHandler(IMessageHandler inner, int failures) : IMessageHandler
{
    private int _remaining = failures;

    public Task<HandleOutcome> HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken) =>
        Interlocked.Decrement(ref _remaining) >= 0
            ? throw new TimeoutException("simulated: MongoDB unavailable")
            : inner.HandleAsync(body, cancellationToken);
}
