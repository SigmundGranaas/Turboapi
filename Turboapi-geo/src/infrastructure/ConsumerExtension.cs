using Turboapi_geo.domain.events;

namespace Turboapi_geo.infrastructure;

public static class KafkaConsumerExtensions
{
    public static IServiceCollection AddKafkaConsumer<TEvent, THandler>(
        this IServiceCollection services,
        string topic,
        string groupId)
        where TEvent : DomainEvent
        where THandler : class, ILocationEventHandler<TEvent>
    {
        // Register the handler
        services.AddScoped<ILocationEventHandler<TEvent>, THandler>();
        
        // Register the consumer configuration
        var config = new KafkaConsumerConfig<TEvent>
        {
            Topic = topic,
            GroupId = groupId
        };
        
        services.AddSingleton(config);
        
        // Register the consumer as a hosted service
        services.AddHostedService<GenericKafkaConsumer<TEvent>>();

        return services;
    }
}