using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrafficEvents.Infrastructure.Hosting;

namespace TrafficEvents.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [HealthTags.Ready]);
        return services;
    }
}
