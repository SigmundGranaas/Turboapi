using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Turbo_event.kafka;
using Turboapi.Infrastructure.Kafka;
using Xunit;

namespace Turboapi.Tests
{
    public class KafkaConsumerIntegrationTests : IAsyncLifetime
    {
        private readonly KafkaTestUtilities _kafkaUtils = new();
        private const string TOPIC_NAME = "test-events";
        private Infrastructure.Kafka.KafkaConsumer<TestEvent>? _consumer;
        private readonly EventTracker<TestEvent> _processedEvents = new();
        
        // Test event class
        public record TestEvent : Event
        {
            public string EventType => nameof(TestEvent);
            public string Id { get; set; } = "";
            public string Data { get; set; } = "";
        }
        
        public async Task InitializeAsync()
        {
            await _kafkaUtils.InitializeContainerAsync();

            // Setup DI container for testing
            var services = _kafkaUtils.CreateBaseServiceCollection();
            
            // Register consumer config
            services.AddSingleton(new KafkaConsumerConfig<TestEvent>
            {
                Topic = TOPIC_NAME,
                GroupId = "test-group"
            });

            // Register test event handler that adds events to our collection
            services.AddSingleton<IEventHandler<TestEvent>>(sp => 
                new TrackedEventHandler<TestEvent>(_processedEvents, 
                    sp.GetRequiredService<ILogger<TrackedEventHandler<TestEvent>>>()));
            
            _kafkaUtils.InitializeServiceProvider(services);
            
            // Create the consumer
            _consumer = _kafkaUtils.CreateConsumer(
                _kafkaUtils.ServiceProvider!.GetRequiredService<IEventHandler<TestEvent>>(),
                _kafkaUtils.ServiceProvider!.GetRequiredService<KafkaConsumerConfig<TestEvent>>());
            
            // Start the consumer
            await _consumer.StartAsync(_kafkaUtils.Cts.Token);
            
            // Give some time for the consumer to start and topic to be created
            await Task.Delay(2000);
        }

        public async Task DisposeAsync()
        {
            if (_consumer != null)
            {
                await _consumer.StopAsync(CancellationToken.None);
            }
            
            await _kafkaUtils.DisposeAsync();
        }

        [Fact]
        public async Task Consumer_ShouldProcessMessages_WhenProduced()
        {
            // Arrange
            _processedEvents.Clear();
            var testEvent = new TestEvent { Id = "1", Data = "Test Data" };

            // Act
            await _kafkaUtils.PublishEventAsync(TOPIC_NAME, testEvent);

            // Assert
            await KafkaTestUtilities.WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
            _processedEvents.GetEvents().Select(x => x.Data).Should().ContainSingle()
                .Which.Should().BeEquivalentTo(testEvent.Data);
        }

        [Fact]
        public async Task Consumer_ShouldHandleMultipleMessages_InOrder()
        {
            // Arrange
            _processedEvents.Clear();
            var events = Enumerable.Range(1, 5)
                .Select(i => new TestEvent { Id = i.ToString(), Data = $"Data {i}" })
                .ToList();

            // Act
            foreach (var @event in events)
            {
                await _kafkaUtils.PublishEventAsync(TOPIC_NAME, @event);
            }

            // Assert
            await KafkaTestUtilities.WaitForConditionAsync(() => _processedEvents.Count == 5, TimeSpan.FromSeconds(10));
            _processedEvents.GetEvents().Select(e => e.Id).Should().BeEquivalentTo(events.Select(e => e.Id));
        }

        [Fact]
        public async Task Consumer_ShouldIgnoreMessagesWithWrongKey()
        {
            // Arrange
            _processedEvents.Clear();
            var testEvent = new TestEvent { Id = "1", Data = "Test Data" };
            var serializedEvent = JsonSerializer.Serialize(testEvent);

            // Act - Send with wrong event type key
            await _kafkaUtils.PublishRawMessageAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = "WrongEventType", 
                    Value = serializedEvent 
                });

            // Wait a bit to ensure message had time to be processed if it was going to be
            await Task.Delay(2000);

            // Assert - Event should not be processed
            _processedEvents.GetEvents().Should().BeEmpty();

            // Now send with correct key and verify it gets processed
            await _kafkaUtils.PublishRawMessageAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = nameof(TestEvent), 
                    Value = serializedEvent 
                });

            await KafkaTestUtilities.WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Consumer_ShouldHandleInvalidJson_Gracefully()
        {
            // Arrange - Send invalid JSON
            _processedEvents.Clear();
            await _kafkaUtils.PublishRawMessageAsync(TOPIC_NAME, 
                new Message<string, string> 
                { 
                    Key = nameof(TestEvent), 
                    Value = "this is not valid json" 
                });

            // Give time for message processing
            await Task.Delay(2000);

            // Verify no events were processed
            _processedEvents.GetEvents().Should().BeEmpty();

            // Now send valid event and verify it's processed
            var validEvent = new TestEvent { Id = "valid", Data = "Valid data" };
            await _kafkaUtils.PublishEventAsync(TOPIC_NAME, validEvent);

            await KafkaTestUtilities.WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
            _processedEvents.GetEvents().First().Id.Should().Be("valid");
        }
    }
}