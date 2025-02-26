using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FluentAssertions;
using System.Text.Json;
using Turbo_event.kafka;
using Turboapi.Infrastructure.Kafka;
using Xunit;

namespace Turboapi.Tests
{
    // This test class demonstrates how to properly test a consumer restart scenario
    public class KafkaConsumerRestartTest : IAsyncLifetime
    {
        private readonly KafkaTestUtilities _kafkaUtils = new();
        private const string TOPIC_NAME = "restart-test-topic";
        
        // Event tracking collection 
        private readonly List<TestEvent> _processedEvents = new();
        
        // Test event type
        public record TestEvent : Event
        {
            public string Name { get; init; } = "";
            public string Data { get; init; } = "";
        }
        
        public async Task InitializeAsync()
        {
            await _kafkaUtils.InitializeContainerAsync();
            
            // Give time for Kafka to fully start
            await Task.Delay(2000);
        }

        public async Task DisposeAsync()
        {
            await _kafkaUtils.DisposeAsync();
        }

        [Fact]
        public async Task Consumer_AfterBeingRecreated_ShouldContinueProcessing()
        {
            // Arrange - Create and configure the first consumer
            var services1 = new ServiceCollection();
            services1.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
            
            services1.AddSingleton(Options.Create(new KafkaSettings
            {
                BootstrapServers = _kafkaUtils.Kafka.GetBootstrapAddress(),
            }));
            
            services1.AddSingleton(_processedEvents);
            services1.AddSingleton<ITopicInitializer, SimpleKafkaTopicInitializer>();
            services1.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
            services1.AddSingleton<IEventHandler<TestEvent>>(sp => 
                new TrackedEventHandler<TestEvent>(
                    new EventTracker<TestEvent>(_processedEvents),
                    sp.GetRequiredService<ILogger<TrackedEventHandler<TestEvent>>>()
                ));
            
            services1.AddSingleton(new KafkaConsumerConfig<TestEvent>
            {
                Topic = TOPIC_NAME,
                GroupId = "test-restart-group"
            });
            
            var serviceProvider1 = services1.BuildServiceProvider();
            
            var consumer1 = new Infrastructure.Kafka.KafkaConsumer<TestEvent>(
                serviceProvider1.GetRequiredService<IEventHandler<TestEvent>>(),
                serviceProvider1.GetRequiredService<KafkaConsumerConfig<TestEvent>>(),
                serviceProvider1.GetRequiredService<IOptions<KafkaSettings>>(),
                serviceProvider1.GetRequiredService<ITopicInitializer>(),
                serviceProvider1.GetRequiredService<IKafkaConsumerFactory>(),
                serviceProvider1.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<TestEvent>>>());
            
            // Start first consumer
            Console.WriteLine("Starting first consumer");
            var cts1 = new CancellationTokenSource();
            await consumer1.StartAsync(cts1.Token);
            
            // Wait for consumer to start and create the topic
            await Task.Delay(3000);
            
            // Send initial event
            var initialEvent = new TestEvent 
            { 
                Name = "Initial Event", 
                Data = "First consumer data" 
            };
            
            await _kafkaUtils.PublishRawMessageAsync(TOPIC_NAME, 
                new Confluent.Kafka.Message<string, string> 
                { 
                    Key = nameof(TestEvent),
                    Value = JsonSerializer.Serialize(initialEvent) 
                });
            
            // Wait for it to be processed
            await KafkaTestUtilities.WaitForConditionAsync(() => _processedEvents.Count >= 1, TimeSpan.FromSeconds(10));
            
            Console.WriteLine("Initial event processed by first consumer");
            
            // Stop and dispose the first consumer
            await consumer1.StopAsync(CancellationToken.None);
            consumer1.Dispose();
            cts1.Cancel();
            cts1.Dispose();
            (serviceProvider1 as IDisposable)?.Dispose();
            
            Console.WriteLine("First consumer stopped and disposed");
            
            // Wait a bit before creating the new consumer
            await Task.Delay(5000);
            
            // Act - Create a completely new consumer with a new service provider
            var services2 = new ServiceCollection();
            services2.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
            
            services2.AddSingleton(Options.Create(new KafkaSettings
            {
                BootstrapServers = _kafkaUtils.Kafka.GetBootstrapAddress(),
            }));
            
            services2.AddSingleton(_processedEvents);
            services2.AddSingleton<ITopicInitializer, SimpleKafkaTopicInitializer>();
            services2.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
            services2.AddSingleton<IEventHandler<TestEvent>>(sp => 
                new TrackedEventHandler<TestEvent>(
                    new EventTracker<TestEvent>(_processedEvents),
                    sp.GetRequiredService<ILogger<TrackedEventHandler<TestEvent>>>()
                ));
            
            services2.AddSingleton(new KafkaConsumerConfig<TestEvent>
            {
                Topic = TOPIC_NAME,
                GroupId = "test-restart-group"  // Same group ID to continue from where the previous consumer left off
            });
            
            var serviceProvider2 = services2.BuildServiceProvider();
            var consumer2 = new Infrastructure.Kafka.KafkaConsumer<TestEvent>(
                serviceProvider2.GetRequiredService<IEventHandler<TestEvent>>(),
                serviceProvider2.GetRequiredService<KafkaConsumerConfig<TestEvent>>(),
                serviceProvider2.GetRequiredService<IOptions<KafkaSettings>>(),
                serviceProvider2.GetRequiredService<ITopicInitializer>(),
                serviceProvider2.GetRequiredService<IKafkaConsumerFactory>(),
                serviceProvider2.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<TestEvent>>>());
            
            // Start second consumer
            Console.WriteLine("Starting second consumer");
            var cts2 = new CancellationTokenSource();
            await consumer2.StartAsync(cts2.Token);
            
            // Wait for consumer to start
            await Task.Delay(5000);
            
            // Send new event
            var newEvent = new TestEvent 
            { 
                Name = "After Restart Event", 
                Data = "Second consumer data" 
            };
            
            await _kafkaUtils.PublishRawMessageAsync(TOPIC_NAME, 
                new Confluent.Kafka.Message<string, string> 
                { 
                    Key = nameof(TestEvent),
                    Value = JsonSerializer.Serialize(newEvent) 
                });
            
            Console.WriteLine("Sent event to second consumer");
            
            // Assert - Wait for the new event to be processed
            await KafkaTestUtilities.WaitForConditionAsync(() => 
            {
                var count = _processedEvents.Count;
                var hasInitial = _processedEvents.Any(e => e.Name == initialEvent.Name);
                var hasNew = _processedEvents.Any(e => e.Name == newEvent.Name);
                
                Console.WriteLine($"Current state: Count={count}, HasInitial={hasInitial}, HasNew={hasNew}");
                
                return _processedEvents.Count >= 2 && 
                       _processedEvents.Any(e => e.Name == initialEvent.Name) &&
                       _processedEvents.Any(e => e.Name == newEvent.Name);
            }, TimeSpan.FromSeconds(15));
            
            // Final verification
            _processedEvents.Should().HaveCountGreaterThanOrEqualTo(2);
            _processedEvents.Should().Contain(e => e.Name == initialEvent.Name);
            _processedEvents.Should().Contain(e => e.Name == newEvent.Name);
            
            // Clean up
            await consumer2.StopAsync(CancellationToken.None);
            consumer2.Dispose();
            cts2.Cancel();
            cts2.Dispose();
            (serviceProvider2 as IDisposable)?.Dispose();
        }
    }
}