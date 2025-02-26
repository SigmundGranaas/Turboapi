using Turboapi.Infrastructure.Kafka;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Turbo_event.kafka;

namespace Turboapi.Tests
{
    /// <summary>
    /// Helper for creating test consumers with standardized configuration
    /// </summary>
    public class KafkaTestConsumerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly List<IDisposable> _disposables;
        
        public KafkaTestConsumerFactory(
            IServiceProvider serviceProvider, 
            JsonSerializerOptions serializerOptions,
            List<IDisposable> disposables)
        {
            _serviceProvider = serviceProvider;
            _serializerOptions = serializerOptions;
            _disposables = disposables;
        }
        
        public Infrastructure.Kafka.KafkaConsumer<TEvent> CreateConsumer<TEvent>(
            IEventHandler<TEvent> handler,
            string topic,
            string groupId) where TEvent : Event
        {
            var config = new KafkaConsumerConfig<TEvent>
            {
                Topic = topic,
                GroupId = groupId
            };
            
            var consumer = new Infrastructure.Kafka.KafkaConsumer<TEvent>(
                handler,
                config,
                _serviceProvider.GetRequiredService<IOptions<KafkaSettings>>(),
                _serviceProvider.GetRequiredService<ITopicInitializer>(),
                _serviceProvider.GetRequiredService<IKafkaConsumerFactory>(),
                _serviceProvider.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<TEvent>>>());
                
            _disposables.Add(consumer);
            return consumer;
        }
        
        public Infrastructure.Kafka.KafkaConsumer<TEvent> CreateTrackedConsumer<TEvent>(
            EventTracker<TEvent> tracker,
            string topic,
            string groupId) where TEvent : Event
        {
            var handler = new TrackedEventHandler<TEvent>(
                tracker, 
                _serviceProvider.GetRequiredService<ILogger<TrackedEventHandler<TEvent>>>());
                
            return CreateConsumer(handler, topic, groupId);
        }
    }
}