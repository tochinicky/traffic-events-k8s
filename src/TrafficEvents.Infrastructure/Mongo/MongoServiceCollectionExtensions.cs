using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using TrafficEvents.Infrastructure.Hosting;

namespace TrafficEvents.Infrastructure.Mongo;

public static class MongoServiceCollectionExtensions
{
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration configuration)
    {
        MongoConventions.Register();

        var connectionString = configuration.GetConnectionString(MongoOptions.ConnectionStringName)
            ?? "mongodb://localhost:27017";
        var database = configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>()?.Database ?? "traffic";

        services.AddSingleton<IMongoClient>(_ =>
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            // Fail fast when Mongo is down, so the message gets retried and readiness flips quickly.
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            settings.ConnectTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(database));
        services.AddSingleton<TrafficCollections>();
        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb", tags: [HealthTags.Ready]);
        return services;
    }
}
