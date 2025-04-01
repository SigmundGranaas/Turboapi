using Microsoft.Extensions.DependencyInjection;

namespace Turboapi.Infrastructure.Kafka;

public static class KafkaConsumerExtensions
{
    public static IServiceCollection AddKafkaConsumer<TEvent, THandler>(
        this IServiceCollection services,
        string topic,
        string groupId)
        where TEvent : Event
        where THandler : class, IEventHandler<TEvent>
    {
        // Register the handler
        services.AddScoped<IEventHandler<TEvent>, THandler>();
        
        // Register the consumer configuration
        var config = new KafkaConsumerConfig<TEvent>
        {
            Topic = topic,
            GroupId = groupId
        };
        
        services.AddSingleton(config);
        
        // Register the consumer as a hosted service
        services.AddHostedService<ScopedKafkaConsumer<TEvent>>();

        return services;
    }
}