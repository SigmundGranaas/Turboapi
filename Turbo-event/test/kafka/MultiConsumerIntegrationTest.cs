using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Turbo_event.kafka;
using Turboapi.Infrastructure.Kafka;
using Xunit;

namespace Turboapi.Tests
{
    public class MultiConsumerIntegrationTests : IAsyncLifetime
    {
        private readonly KafkaTestUtilities _kafkaUtils = new();
        private const string SHARED_TOPIC = "shared-events-topic";
        
        // Event tracking collections for each consumer
        private readonly EventTracker<LocationCreatedEvent> _locationCreatedEvents = new();
        private readonly EventTracker<UserRegisteredEvent> _userRegisteredEvents = new();
        private readonly EventTracker<OrderPlacedEvent> _orderPlacedEvents = new();
        
        // Consumers
        private Infrastructure.Kafka.KafkaConsumer<LocationCreatedEvent>? _locationConsumer;
        private Infrastructure.Kafka.KafkaConsumer<UserRegisteredEvent>? _userConsumer;
        private Infrastructure.Kafka.KafkaConsumer<OrderPlacedEvent>? _orderConsumer;
        
        // Event types
        public record LocationCreatedEvent : Event
        {
            public string Name { get; init; } = "";
            public double Latitude { get; init; }
            public double Longitude { get; init; }
        }
        
        public record UserRegisteredEvent : Event
        {
            public string Username { get; init; } = "";
            public string Email { get; init; } = "";
        }
        
        public record OrderPlacedEvent : Event
        {
            public string OrderNumber { get; init; } = "";
            public decimal Amount { get; init; }
            public string CustomerId { get; init; } = "";
        }
        
        public async Task InitializeAsync()
        {
            await _kafkaUtils.InitializeContainerAsync();
            
            // Setup services
            var services = _kafkaUtils.CreateBaseServiceCollection();
            
            // Register the event trackers
            services.AddSingleton(_locationCreatedEvents);
            services.AddSingleton(_userRegisteredEvents);
            services.AddSingleton(_orderPlacedEvents);
            
            // Register event handlers
            services.AddSingleton<IEventHandler<LocationCreatedEvent>>(sp => 
                new TrackedEventHandler<LocationCreatedEvent>(_locationCreatedEvents, 
                    sp.GetRequiredService<ILogger<TrackedEventHandler<LocationCreatedEvent>>>()));
                
            services.AddSingleton<IEventHandler<UserRegisteredEvent>>(sp => 
                new TrackedEventHandler<UserRegisteredEvent>(_userRegisteredEvents, 
                    sp.GetRequiredService<ILogger<TrackedEventHandler<UserRegisteredEvent>>>()));
                
            services.AddSingleton<IEventHandler<OrderPlacedEvent>>(sp => 
                new TrackedEventHandler<OrderPlacedEvent>(_orderPlacedEvents, 
                    sp.GetRequiredService<ILogger<TrackedEventHandler<OrderPlacedEvent>>>()));
            
            // Register consumer configs - all using the same topic but different group IDs
            services.AddSingleton(new KafkaConsumerConfig<LocationCreatedEvent>
            {
                Topic = SHARED_TOPIC,
                GroupId = "location-consumer-group"
            });
            
            services.AddSingleton(new KafkaConsumerConfig<UserRegisteredEvent>
            {
                Topic = SHARED_TOPIC,
                GroupId = "user-consumer-group"
            });
            
            services.AddSingleton(new KafkaConsumerConfig<OrderPlacedEvent>
            {
                Topic = SHARED_TOPIC,
                GroupId = "order-consumer-group"
            });
            
            _kafkaUtils.InitializeServiceProvider(services);
            
            // Setup registry and serializer options
            var registry = new EventTypeRegistry();
            registry.RegisterEventType<LocationCreatedEvent>();
            registry.RegisterEventType<UserRegisteredEvent>();
            registry.RegisterEventType<OrderPlacedEvent>();
            _kafkaUtils.CreateJsonSerializerOptions(registry);
            
            // Create the consumers
            _locationConsumer = _kafkaUtils.CreateConsumer(
                _kafkaUtils.ServiceProvider!.GetRequiredService<IEventHandler<LocationCreatedEvent>>(),
                _kafkaUtils.ServiceProvider!.GetRequiredService<KafkaConsumerConfig<LocationCreatedEvent>>()
            );
            
            _userConsumer = _kafkaUtils.CreateConsumer(
                _kafkaUtils.ServiceProvider!.GetRequiredService<IEventHandler<UserRegisteredEvent>>(),
                _kafkaUtils.ServiceProvider!.GetRequiredService<KafkaConsumerConfig<UserRegisteredEvent>>()
            );
            
            _orderConsumer = _kafkaUtils.CreateConsumer(
                _kafkaUtils.ServiceProvider!.GetRequiredService<IEventHandler<OrderPlacedEvent>>(),
                _kafkaUtils.ServiceProvider!.GetRequiredService<KafkaConsumerConfig<OrderPlacedEvent>>()
            );
            
            // Start all consumers
            await Task.WhenAll(
                _locationConsumer.StartAsync(_kafkaUtils.Cts.Token),
                _userConsumer.StartAsync(_kafkaUtils.Cts.Token),
                _orderConsumer.StartAsync(_kafkaUtils.Cts.Token)
            );
            
            // Give time for consumers to start
            await Task.Delay(2000);
        }

        public async Task DisposeAsync()
        {
            if (_locationConsumer != null)
                await _locationConsumer.StopAsync(CancellationToken.None);
                
            if (_userConsumer != null)
                await _userConsumer.StopAsync(CancellationToken.None);
                
            if (_orderConsumer != null)
                await _orderConsumer.StopAsync(CancellationToken.None);
            
            await _kafkaUtils.DisposeAsync();
        }

        [Fact]
        public async Task EachConsumer_ShouldOnlyProcess_ItsOwnEventType()
        {
            // Arrange - Create one of each event type
            var locationEvent = new LocationCreatedEvent 
            { 
                Name = "Test Location", 
                Latitude = 37.7749, 
                Longitude = -122.4194 
            };
            
            var userEvent = new UserRegisteredEvent 
            { 
                Username = "testuser", 
                Email = "test@example.com" 
            };
            
            var orderEvent = new OrderPlacedEvent 
            { 
                OrderNumber = "ORD-12345", 
                Amount = 99.99m, 
                CustomerId = "CUST-789" 
            };

            // Act - Publish all events to the same topic
            await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, locationEvent);
            await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, userEvent);
            await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, orderEvent);

            // Assert - Wait for all events to be processed by their respective consumers
            await KafkaTestUtilities.WaitForConditionAsync(() => 
                _locationCreatedEvents.Count >= 1 && 
                _userRegisteredEvents.Count >= 1 && 
                _orderPlacedEvents.Count >= 1, 
                TimeSpan.FromSeconds(15));

            // Verify each consumer processed only its own event type
            _locationCreatedEvents.GetEvents().Should().HaveCount(1);
            _locationCreatedEvents.GetEvents()[0].Name.Should().Be(locationEvent.Name);
            
            _userRegisteredEvents.GetEvents().Should().HaveCount(1);
            _userRegisteredEvents.GetEvents()[0].Username.Should().Be(userEvent.Username);
            
            _orderPlacedEvents.GetEvents().Should().HaveCount(1);
            _orderPlacedEvents.GetEvents()[0].OrderNumber.Should().Be(orderEvent.OrderNumber);
        }

        [Fact]
        public async Task MultipleEventsOfSameType_ShouldAllBeProcessed_ByRespectiveConsumer()
        {
            // Arrange - Clear previous events
            _locationCreatedEvents.Clear();
            _userRegisteredEvents.Clear();
            _orderPlacedEvents.Clear();
            
            // Create multiple events of each type
            var locationEvents = Enumerable.Range(1, 5)
                .Select(i => new LocationCreatedEvent 
                { 
                    Name = $"Location {i}", 
                    Latitude = 35 + i, 
                    Longitude = -120 - i 
                })
                .ToList();
                
            var userEvents = Enumerable.Range(1, 3)
                .Select(i => new UserRegisteredEvent 
                { 
                    Username = $"user{i}", 
                    Email = $"user{i}@example.com" 
                })
                .ToList();
                
            var orderEvents = Enumerable.Range(1, 4)
                .Select(i => new OrderPlacedEvent 
                { 
                    OrderNumber = $"ORD-{1000+i}", 
                    Amount = 50m * i, 
                    CustomerId = $"CUST-{i}" 
                })
                .ToList();

            // Act - Publish all events in an interleaved fashion
            for (int i = 0; i < 5; i++)
            {
                // Publish a location event
                if (i < locationEvents.Count)
                {
                    await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, locationEvents[i]);
                }
                
                // Publish a user event
                if (i < userEvents.Count)
                {
                    await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, userEvents[i]);
                }
                
                // Publish an order event
                if (i < orderEvents.Count)
                {
                    await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, orderEvents[i]);
                }
            }

            // Assert - Wait for all events to be processed
            await KafkaTestUtilities.WaitForConditionAsync(() => 
                _locationCreatedEvents.Count >= locationEvents.Count && 
                _userRegisteredEvents.Count >= userEvents.Count && 
                _orderPlacedEvents.Count >= orderEvents.Count, 
                TimeSpan.FromSeconds(15));

            // Verify each consumer processed all of its event types
            _locationCreatedEvents.GetEvents().Should().HaveCount(locationEvents.Count);
            var locationNames = _locationCreatedEvents.GetEvents().Select(e => e.Name).OrderBy(n => n).ToList();
            locationNames.Should().BeEquivalentTo(locationEvents.Select(e => e.Name).OrderBy(n => n));
            
            _userRegisteredEvents.GetEvents().Should().HaveCount(userEvents.Count);
            var usernames = _userRegisteredEvents.GetEvents().Select(e => e.Username).OrderBy(u => u).ToList();
            usernames.Should().BeEquivalentTo(userEvents.Select(e => e.Username).OrderBy(u => u));
            
            _orderPlacedEvents.GetEvents().Should().HaveCount(orderEvents.Count);
            var orderNumbers = _orderPlacedEvents.GetEvents().Select(e => e.OrderNumber).OrderBy(o => o).ToList();
            orderNumbers.Should().BeEquivalentTo(orderEvents.Select(e => e.OrderNumber).OrderBy(o => o));
        }
        
        [Fact]
        public async Task InvalidEventData_ShouldNotImpact_ProcessingOfValidEvents()
        {
            // Arrange - Clear previous events
            _locationCreatedEvents.Clear();
            _userRegisteredEvents.Clear();
            _orderPlacedEvents.Clear();
            
            // Send invalid JSON for each event type
            await _kafkaUtils.PublishRawMessageAsync(SHARED_TOPIC, 
                new Confluent.Kafka.Message<string, string> 
                { 
                    Key = nameof(LocationCreatedEvent),
                    Value = "{ invalid json for location }" 
                });
                
            await _kafkaUtils.PublishRawMessageAsync(SHARED_TOPIC, 
                new Confluent.Kafka.Message<string, string> 
                { 
                    Key = nameof(UserRegisteredEvent),
                    Value = "not valid json" 
                });
                
            await _kafkaUtils.PublishRawMessageAsync(SHARED_TOPIC, 
                new Confluent.Kafka.Message<string, string> 
                { 
                    Key = nameof(OrderPlacedEvent),
                    Value = null 
                });
                
            // Wait a bit to ensure invalid messages were processed
            await Task.Delay(2000);
            
            // No events should have been processed
            _locationCreatedEvents.GetEvents().Should().BeEmpty();
            _userRegisteredEvents.GetEvents().Should().BeEmpty();
            _orderPlacedEvents.GetEvents().Should().BeEmpty();
            
            // Act - Now send valid events
            var validLocation = new LocationCreatedEvent 
            { 
                Name = "Valid Location", 
                Latitude = 51.5074, 
                Longitude = -0.1278 
            };
            
            var validUser = new UserRegisteredEvent 
            { 
                Username = "validuser", 
                Email = "valid@example.com" 
            };
            
            var validOrder = new OrderPlacedEvent 
            { 
                OrderNumber = "ORD-VALID", 
                Amount = 199.99m, 
                CustomerId = "VALID-CUST" 
            };
            
            await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, validLocation);
            await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, validUser);
            await _kafkaUtils.PublishEventAsync(SHARED_TOPIC, validOrder);
                
            // Assert - Valid events should be processed
            await KafkaTestUtilities.WaitForConditionAsync(() => 
                _locationCreatedEvents.Count >= 1 && 
                _userRegisteredEvents.Count >= 1 && 
                _orderPlacedEvents.Count >= 1, 
                TimeSpan.FromSeconds(10));
                
            // Verify each consumer processed its valid event
            _locationCreatedEvents.GetEvents().Should().ContainSingle()
                .Which.Name.Should().Be(validLocation.Name);
            
            _userRegisteredEvents.GetEvents().Should().ContainSingle()
                .Which.Username.Should().Be(validUser.Username);
            
            _orderPlacedEvents.GetEvents().Should().ContainSingle()
                .Which.OrderNumber.Should().Be(validOrder.OrderNumber);
        }
    }
}