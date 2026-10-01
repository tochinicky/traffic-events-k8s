using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;

namespace TrafficEvents.IntegrationTests;

/// <summary>
/// One RabbitMQ and one MongoDB container for the whole run (same major versions as the cluster).
/// Tests share fixed queue names, so they run sequentially in one collection and each starts by purging.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:4.1-management").Build();
    public MongoDbContainer MongoDb { get; } = new MongoDbBuilder("mongo:7").Build();

    public Task InitializeAsync() => Task.WhenAll(RabbitMq.StartAsync(), MongoDb.StartAsync());

    public async Task DisposeAsync()
    {
        await RabbitMq.DisposeAsync();
        await MongoDb.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "infrastructure";
}
