using TrafficEvents.Infrastructure.Hosting;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.Infrastructure.Mongo;

namespace TrafficEvents.Processor;

public static class ProcessorServiceCollectionExtensions
{
    /// <summary>Everything the processor needs except the hosted service (tests drive the consumer directly).</summary>
    public static IServiceCollection AddProcessor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProcessorOptions>(configuration.GetSection(ProcessorOptions.SectionName));
        services.AddRabbitMq(configuration);
        services.AddMongo(configuration);
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<EventStore>();
        services.AddSingleton<JunctionDirectory>();
        services.AddSingleton<AlertService>();
        services.AddSingleton<IMessageHandler, TrafficEventHandler>();
        services.AddSingleton<EventConsumer>();
        services.AddHealthChecks().AddCheck<ConsumerHealthCheck>("consumer", tags: [HealthTags.Ready]);
        return services;
    }
}
