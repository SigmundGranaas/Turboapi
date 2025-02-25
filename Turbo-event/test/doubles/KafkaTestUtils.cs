
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Testcontainers.Kafka;
using Turbo_event.kafka;
using Turboapi.Infrastructure.Kafka;

namespace Turboapi.Tests
{
    /// <summary>
    /// Provides utility methods for Kafka integration tests
    /// </summary>
    public class KafkaTestUtilities : IAsyncDisposable
    {
        public KafkaContainer Kafka { get; }
        public IProducer<string, string>? Producer { get; private set; }
        public IServiceProvider? ServiceProvider { get; private set; }
        public CancellationTokenSource Cts { get; } = new();
        public List<IDisposable> Disposables { get; } = new();
        public JsonSerializerOptions? SerializerOptions { get; private set; }
        
        public KafkaTestUtilities()
        {
            Kafka = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:6.2.10")
                .WithPortBinding(9092, true)
                .Build();
        }
        
        public async Task InitializeContainerAsync()
        {
            await Kafka.StartAsync();
            
            // Create the producer
            Producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = Kafka.GetBootstrapAddress()
            }).Build();
            Disposables.Add(Producer);
            
            // Give some time for Kafka to fully start
            await Task.Delay(1000);
        }
        
        public IServiceCollection CreateBaseServiceCollection()
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
            
            // Register Kafka settings with bootstrap server from container
            services.AddSingleton(Options.Create(new KafkaSettings
            {
                BootstrapServers = Kafka.GetBootstrapAddress(),
            }));
            
            // Register topic initializer and consumer factory
            services.AddSingleton<ITopicInitializer, SimpleKafkaTopicInitializer>();
            services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
            
            return services;
        }
        
        public void InitializeServiceProvider(IServiceCollection services)
        {
            ServiceProvider = services.BuildServiceProvider();
            Disposables.Add((ServiceProvider as IDisposable)!);
        }
        
        public JsonSerializerOptions CreateJsonSerializerOptions(EventTypeRegistry registry)
        {
            var converter = new EventJsonConverter(registry);
            var options = new JsonSerializerOptions();
            options.Converters.Add(converter);
            SerializerOptions = options;
            return options;
        }
        
        public async Task<DeliveryResult<string, string>> PublishEventAsync<TEvent>(string topic, TEvent @event) 
            where TEvent : Event
        {
            if (Producer == null)
                throw new InvalidOperationException("Producer has not been initialized");
            
            if (SerializerOptions == null)
                throw new InvalidOperationException("SerializerOptions has not been initialized");
                
            return await Producer.ProduceAsync(topic, 
                new Message<string, string> 
                { 
                    Key = typeof(TEvent).Name,
                    Value = JsonSerializer.Serialize(@event, SerializerOptions) 
                });
        }
        
        public async Task<DeliveryResult<string, string>> PublishRawMessageAsync(string topic, Message<string, string> message)
        {
            if (Producer == null)
                throw new InvalidOperationException("Producer has not been initialized");
                
            return await Producer.ProduceAsync(topic, message);
        }
        
        public Infrastructure.Kafka.KafkaConsumer<TEvent> CreateConsumer<TEvent>(
            IEventHandler<TEvent> handler,
            KafkaConsumerConfig<TEvent> config) where TEvent : Event
        {
            if (ServiceProvider == null)
                throw new InvalidOperationException("ServiceProvider has not been initialized");
                
            if (SerializerOptions == null)
                throw new InvalidOperationException("SerializerOptions has not been initialized");
                
            var consumer = new Infrastructure.Kafka.KafkaConsumer<TEvent>(
                handler,
                config,
                ServiceProvider.GetRequiredService<IOptions<KafkaSettings>>(),
                ServiceProvider.GetRequiredService<ITopicInitializer>(),
                ServiceProvider.GetRequiredService<IKafkaConsumerFactory>(),
                SerializerOptions,
                ServiceProvider.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<TEvent>>>());
                
            Disposables.Add(consumer);
            return consumer;
        }
        
        public static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var logInterval = TimeSpan.FromSeconds(2);
            var nextLogTime = logInterval;
            
            while (sw.Elapsed < timeout)
            {
                if (condition())
                    return;
                
                // Log periodically to show progress
                if (sw.Elapsed > nextLogTime)
                {
                    Console.WriteLine($"Still waiting for condition after {sw.Elapsed.TotalSeconds:F1} seconds...");
                    nextLogTime += logInterval;
                }
                
                await Task.Delay(250);
            }
            
            throw new TimeoutException($"Condition not met within {timeout.TotalSeconds} seconds");
        }
        
        public async ValueTask DisposeAsync()
        {
            Cts.Cancel();
            
            foreach (var disposable in Disposables)
            {
                disposable.Dispose();
            }
            
            Cts.Dispose();
            
            await Kafka.DisposeAsync();
        }
    }
}