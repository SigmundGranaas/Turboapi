using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Testcontainers.Kafka;
using Turbo_event.kafka;
using Turboapi.Infrastructure.Kafka;
using Xunit;

namespace Turboapi.Tests
{
    public class KafkaConsumerIntegrationTests : IAsyncLifetime
    {
        private readonly KafkaContainer _kafka;
        private const string TOPIC_NAME = "test-events";
        private Infrastructure.Kafka.KafkaConsumer<TestEvent>? _consumer;
        private IProducer<string, string>? _producer;
        private readonly List<TestEvent> _processedEvents;
        private IServiceProvider? _serviceProvider;
        private readonly CancellationTokenSource _cts;
        
        public KafkaConsumerIntegrationTests()
        {
            _processedEvents = new List<TestEvent>();
            _cts = new CancellationTokenSource();

            _kafka = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:6.2.10")
                .WithPortBinding(9092, true)
                .Build();
        }

        // Test event class
        public record TestEvent : Event
        {
            public string EventType => nameof(TestEvent);
            public string Id { get; set; } = "";
            public string Data { get; set; } = "";
        }
        
        // Test event handler
        public class TestEventHandler : IEventHandler<TestEvent>
        {
            private readonly List<TestEvent> _processedEvents;

            public TestEventHandler(List<TestEvent> processedEvents)
            {
                _processedEvents = processedEvents;
            }

            public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
            {
                _processedEvents.Add(@event);
                return Task.CompletedTask;
            }
        }
        
        public async Task InitializeAsync()
        {
            await _kafka.StartAsync();

            // Setup DI container for testing
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddConsole());
            
            // Register Kafka settings with bootstrap server from container
            services.AddSingleton(Options.Create(new KafkaSettings
            {
                BootstrapServers = _kafka.GetBootstrapAddress(),
            }));

            // Register consumer config
            services.AddSingleton(new KafkaConsumerConfig<TestEvent>
            {
                Topic = TOPIC_NAME,
                GroupId = "test-group"
            });

            // Register test event handler that adds events to our collection
            services.AddSingleton<IEventHandler<TestEvent>>(sp => new TestEventHandler(_processedEvents));
            
            // Register topic initializer and consumer factory
            services.AddSingleton<ITopicInitializer, SimpleKafkaTopicInitializer>();
            services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();

            _serviceProvider = services.BuildServiceProvider();

            var registry = new EventTypeRegistry();
            registry.RegisterEventType<TestEvent>("test");
            var converter1 = new EventJsonConverter(registry);
            var opt = new JsonSerializerOptions();
            opt.Converters.Add(converter1);
            // Create the consumer
            _consumer = new Infrastructure.Kafka.KafkaConsumer<TestEvent>(
                _serviceProvider.GetRequiredService<IEventHandler<TestEvent>>(),
                _serviceProvider.GetRequiredService<KafkaConsumerConfig<TestEvent>>(),
                _serviceProvider.GetRequiredService<IOptions<KafkaSettings>>(),
                _serviceProvider.GetRequiredService<ITopicInitializer>(),
                _serviceProvider.GetRequiredService<IKafkaConsumerFactory>(),
                opt,
                _serviceProvider.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<TestEvent>>>());

            // Create the producer
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _kafka.GetBootstrapAddress()
            }).Build();
            
            // Start the consumer
            _ = _consumer.StartAsync(_cts.Token);
            
            // Give some time for the consumer to start and topic to be created
            await Task.Delay(2000);
        }

        public async Task DisposeAsync()
        {
            _cts.Cancel();
            
            if (_consumer != null)
            {
                await _consumer.StopAsync(CancellationToken.None);
                _consumer.Dispose();
            }
            
            _producer?.Dispose();
            (_serviceProvider as IDisposable)?.Dispose();
            _cts.Dispose();
            
            await _kafka.DisposeAsync();
        }

        [Fact]
        public async Task Consumer_ShouldProcessMessages_WhenProduced()
        {
            // Arrange
            var testEvent = new TestEvent { Id = "1", Data = "Test Data" };
            var serializedEvent = JsonSerializer.Serialize(testEvent);

            // Act
            await _producer!.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = nameof(TestEvent),
                    Value = serializedEvent 
                });

            // Assert
            await WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
            _processedEvents.Select(x => x.Data).Should().ContainSingle()
                .Which.Should().BeEquivalentTo(testEvent.Data);
        }

        [Fact]
        public async Task Consumer_ShouldHandleMultipleMessages_InOrder()
        {
            // Arrange
            var events = Enumerable.Range(1, 5)
                .Select(i => new TestEvent { Id = i.ToString(), Data = $"Data {i}" })
                .ToList();

            // Act
            foreach (var @event in events)
            {
                await _producer!.ProduceAsync(TOPIC_NAME, 
                    new Message<string, string> 
                    { 
                        Key = nameof(TestEvent),
                        Value = JsonSerializer.Serialize(@event) 
                    });
            }

            // Assert
            await WaitForConditionAsync(() => _processedEvents.Count == 5, TimeSpan.FromSeconds(10));
            _processedEvents.Select(e => e.Id).Should().BeEquivalentTo(events.Select(e => e.Id));
        }

        [Fact]
        public async Task Consumer_ShouldIgnoreMessagesWithWrongKey()
        {
            // Arrange
            var testEvent = new TestEvent { Id = "1", Data = "Test Data" };
            var serializedEvent = JsonSerializer.Serialize(testEvent);

            // Act - Send with wrong event type key
            await _producer!.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = "WrongEventType", 
                    Value = serializedEvent 
                });

            // Wait a bit to ensure message had time to be processed if it was going to be
            await Task.Delay(2000);

            // Assert - Event should not be processed
            _processedEvents.Should().BeEmpty();

            // Now send with correct key and verify it gets processed
            await _producer.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = nameof(TestEvent), 
                    Value = serializedEvent 
                });

            await WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Consumer_ShouldHandleInvalidJson_Gracefully()
        {
            // Arrange - Send invalid JSON
            await _producer!.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = nameof(TestEvent), 
                    Value = "this is not valid json" 
                });

            // Give time for message processing
            await Task.Delay(2000);

            // Verify no events were processed
            _processedEvents.Should().BeEmpty();

            // Now send valid event and verify it's processed
            var validEvent = new TestEvent { Id = "valid", Data = "Valid data" };
            await _producer.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = nameof(TestEvent), 
                    Value = JsonSerializer.Serialize(validEvent) 
                });

            await WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
            _processedEvents.First().Id.Should().Be("valid");
        }

        private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (condition())
                    return;
                await Task.Delay(100);
            }
            throw new TimeoutException($"Condition not met within {timeout.TotalSeconds} seconds");
        }
    }
}