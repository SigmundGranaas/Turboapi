using GeoSpatial.Domain.Events;
using Turboapi_geo.domain.events;
using Turboapi_geo.eventbus_adapter;
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
        
        services.AddKafkaConsumer<CreatePositionEvent, CreatePositionCommandEventAdapter>(
            "location.create_command",
            "location-consumers");
        
        services.AddKafkaConsumer<LocationCreated, LocationCreatedHandler>(
            "location-events",
            "location-group-create");
        
        services.AddKafkaConsumer<LocationPositionChanged, LocationPositionChangedHandler>(
            "location-events",
            "location-group-update");
        
        services.AddKafkaConsumer<LocationDeleted, LocationDeletedHandler>(
            "location-events",
            "location-group-delete");
        
 
        return services;
    }
}