using GeoSpatial.Domain.Events;
using Turboapi_geo.domain.events;
using Turboapi.infrastructure;

namespace Turboapi_geo.infrastructure;

public static class KafkaServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaEventInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KafkaSettings>(
            configuration.GetSection("Kafka"));

        // Register the topic initializer
        services.AddSingleton<IKafkaTopicInitializer, KafkaTopicInitializer>();
        
        // Register event infrastructure
        services.AddSingleton<IEventWriter, KafkaEventWriter>();
        
        return services;
    }
}