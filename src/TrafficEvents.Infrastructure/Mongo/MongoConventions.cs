using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace TrafficEvents.Infrastructure.Mongo;

/// <summary>Process-wide BSON settings. Safe to call more than once.</summary>
public static class MongoConventions
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        ConventionRegistry.Register(
            "traffic-events",
            new ConventionPack { new CamelCaseElementNameConvention(), new IgnoreExtraElementsConvention(true) },
            _ => true);

        // Store GUIDs as standard UUID (subtype 4) so they read the same in mongosh and other drivers.
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }
}
