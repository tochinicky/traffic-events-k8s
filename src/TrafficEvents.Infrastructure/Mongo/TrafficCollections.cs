using MongoDB.Driver;

namespace TrafficEvents.Infrastructure.Mongo;

/// <summary>Typed handles to every collection, so names live in one place.</summary>
public sealed class TrafficCollections(IMongoDatabase database)
{
    public const string JunctionsName = "junctions";
    public const string EventsName = "events";
    public const string JunctionStatusName = "junction_status";
    public const string ProcessedEventsName = "processed_events";
    public const string AlertsName = "alerts";

    public IMongoDatabase Database { get; } = database;
    public IMongoCollection<JunctionDocument> Junctions { get; } = database.GetCollection<JunctionDocument>(JunctionsName);
    public IMongoCollection<EventDocument> Events { get; } = database.GetCollection<EventDocument>(EventsName);
    public IMongoCollection<JunctionStatusDocument> JunctionStatus { get; } = database.GetCollection<JunctionStatusDocument>(JunctionStatusName);
    public IMongoCollection<ProcessedEventDocument> ProcessedEvents { get; } = database.GetCollection<ProcessedEventDocument>(ProcessedEventsName);
    public IMongoCollection<AlertDocument> Alerts { get; } = database.GetCollection<AlertDocument>(AlertsName);
}
