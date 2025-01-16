using GeoSpatial.Domain.Events;
using Turboapi.infrastructure;

namespace Turboapi_geo.infrastructure;

public static class KafkaServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaEventInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Kafka settings
        services.Configure<KafkaSettings>(
            configuration.GetSection("Kafka"));


        // Register event infrastructure
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<IEventWriter, KafkaEventStore>();
        services.AddSingleton<IEventSubscriber, KafkaEventSubscriber>();

        return services;
    }
}