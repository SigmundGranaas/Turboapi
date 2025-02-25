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
    public class MultiConsumerIntegrationTests : IAsyncLifetime
    {
        private readonly KafkaContainer _kafka;
        private const string SHARED_TOPIC = "shared-events-topic";
        private List<IDisposable> _disposables = new();
        private IProducer<string, string>? _producer;
        private IServiceProvider? _serviceProvider;
        private readonly CancellationTokenSource _cts = new();
        
        // Event tracking collections for each consumer
        private readonly List<LocationCreatedEvent> _locationCreatedEvents = new();
        private readonly List<UserRegisteredEvent> _userRegisteredEvents = new();
        private readonly List<OrderPlacedEvent> _orderPlacedEvents = new();
        
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
        
        // Event handlers
        public class LocationCreatedHandler : IEventHandler<LocationCreatedEvent>
        {
            private readonly List<LocationCreatedEvent> _events;
            private readonly ILogger<LocationCreatedHandler> _logger;

            public LocationCreatedHandler(List<LocationCreatedEvent> events, ILogger<LocationCreatedHandler> logger)
            {
                _events = events;
                _logger = logger;
            }

            public Task HandleAsync(LocationCreatedEvent @event, CancellationToken cancellationToken)
            {
                _logger.LogInformation("Handling location created: {LocationId} - {Name}", @event.Id, @event.Name);
                lock (_events)
                {
                    _events.Add(@event);
                }
                return Task.CompletedTask;
            }
        }
        
        public class UserRegisteredHandler : IEventHandler<UserRegisteredEvent>
        {
            private readonly List<UserRegisteredEvent> _events;
            private readonly ILogger<UserRegisteredHandler> _logger;

            public UserRegisteredHandler(List<UserRegisteredEvent> events, ILogger<UserRegisteredHandler> logger)
            {
                _events = events;
                _logger = logger;
            }

            public Task HandleAsync(UserRegisteredEvent @event, CancellationToken cancellationToken)
            {
                _logger.LogInformation("Handling user registered: {UserId} - {Username}", @event.Id, @event.Username);
                lock (_events)
                {
                    _events.Add(@event);
                }
                return Task.CompletedTask;
            }
        }
        
        public class OrderPlacedHandler : IEventHandler<OrderPlacedEvent>
        {
            private readonly List<OrderPlacedEvent> _events;
            private readonly ILogger<OrderPlacedHandler> _logger;

            public OrderPlacedHandler(List<OrderPlacedEvent> events, ILogger<OrderPlacedHandler> logger)
            {
                _events = events;
                _logger = logger;
            }

            public Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken)
            {
                _logger.LogInformation("Handling order placed: {OrderId} - {Amount:C}", @event.Id, @event.Amount);
                lock (_events)
                {
                    _events.Add(@event);
                }
                return Task.CompletedTask;
            }
        }
        
        public MultiConsumerIntegrationTests()
        {
            _kafka = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:6.2.10")
                .WithPortBinding(9092, true)
                .Build();
        }

        public async Task InitializeAsync()
        {
            await _kafka.StartAsync();

            // Setup DI container for testing
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
            
            // Kafka settings
            services.AddSingleton(Options.Create(new KafkaSettings
            {
                BootstrapServers = _kafka.GetBootstrapAddress(),

            }));

            // Register the event lists
            services.AddSingleton(_locationCreatedEvents);
            services.AddSingleton(_userRegisteredEvents);
            services.AddSingleton(_orderPlacedEvents);
            
            // Register infrastructure components
            services.AddSingleton<ITopicInitializer, SimpleKafkaTopicInitializer>();
            services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
            
            // Register event handlers
            services.AddSingleton<IEventHandler<LocationCreatedEvent>, LocationCreatedHandler>();
            services.AddSingleton<IEventHandler<UserRegisteredEvent>, UserRegisteredHandler>();
            services.AddSingleton<IEventHandler<OrderPlacedEvent>, OrderPlacedHandler>();
            
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

            _serviceProvider = services.BuildServiceProvider();
            
            // Create event type registry
            var registry = new EventTypeRegistry();
            registry.RegisterEventType<LocationCreatedEvent>();
            registry.RegisterEventType<UserRegisteredEvent>();
            registry.RegisterEventType<OrderPlacedEvent>();
            
            // Create the event converter
            var eventConverter = new EventJsonConverter(registry);
            var converter1 = new EventJsonConverter(registry);
            var opt = new JsonSerializerOptions();
            opt.Converters.Add(converter1);
            // Create the consumers
            _locationConsumer = new Infrastructure.Kafka.KafkaConsumer<LocationCreatedEvent>(
                _serviceProvider.GetRequiredService<IEventHandler<LocationCreatedEvent>>(),
                _serviceProvider.GetRequiredService<KafkaConsumerConfig<LocationCreatedEvent>>(),
                _serviceProvider.GetRequiredService<IOptions<KafkaSettings>>(),
                _serviceProvider.GetRequiredService<ITopicInitializer>(),
                _serviceProvider.GetRequiredService<IKafkaConsumerFactory>(),
                opt,
                _serviceProvider.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<LocationCreatedEvent>>>());
            _disposables.Add(_locationConsumer);
            
            _userConsumer = new Infrastructure.Kafka.KafkaConsumer<UserRegisteredEvent>(
                _serviceProvider.GetRequiredService<IEventHandler<UserRegisteredEvent>>(),
                _serviceProvider.GetRequiredService<KafkaConsumerConfig<UserRegisteredEvent>>(),
                _serviceProvider.GetRequiredService<IOptions<KafkaSettings>>(),
                _serviceProvider.GetRequiredService<ITopicInitializer>(),
                _serviceProvider.GetRequiredService<IKafkaConsumerFactory>(),
                opt,
                _serviceProvider.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<UserRegisteredEvent>>>());
            _disposables.Add(_userConsumer);
            
            _orderConsumer = new Infrastructure.Kafka.KafkaConsumer<OrderPlacedEvent>(
                _serviceProvider.GetRequiredService<IEventHandler<OrderPlacedEvent>>(),
                _serviceProvider.GetRequiredService<KafkaConsumerConfig<OrderPlacedEvent>>(),
                _serviceProvider.GetRequiredService<IOptions<KafkaSettings>>(),
                _serviceProvider.GetRequiredService<ITopicInitializer>(),
                _serviceProvider.GetRequiredService<IKafkaConsumerFactory>(),
                opt,
                _serviceProvider.GetRequiredService<ILogger<Infrastructure.Kafka.KafkaConsumer<OrderPlacedEvent>>>());
            _disposables.Add(_orderConsumer);

            // Create the producer
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _kafka.GetBootstrapAddress()
            }).Build();
            _disposables.Add(_producer);
            
            // Start all consumers
            await Task.WhenAll(
                _locationConsumer.StartAsync(_cts.Token),
                _userConsumer.StartAsync(_cts.Token),
                _orderConsumer.StartAsync(_cts.Token)
            );
            
            // Give time for consumers to start
            await Task.Delay(2000);
        }

        public async Task DisposeAsync()
        {
            _cts.Cancel();
            
            if (_locationConsumer != null)
                await _locationConsumer.StopAsync(CancellationToken.None);
                
            if (_userConsumer != null)
                await _userConsumer.StopAsync(CancellationToken.None);
                
            if (_orderConsumer != null)
                await _orderConsumer.StopAsync(CancellationToken.None);
            
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
            
            (_serviceProvider as IDisposable)?.Dispose();
            _cts.Dispose();
            
            await _kafka.DisposeAsync();
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
            await _producer!.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(LocationCreatedEvent),
                    Value = JsonSerializer.Serialize(locationEvent) 
                });
                
            await _producer.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(UserRegisteredEvent),
                    Value = JsonSerializer.Serialize(userEvent) 
                });
                
            await _producer.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(OrderPlacedEvent),
                    Value = JsonSerializer.Serialize(orderEvent) 
                });

            // Assert - Wait for all events to be processed by their respective consumers
            await WaitForConditionAsync(() => 
                _locationCreatedEvents.Count >= 1 && 
                _userRegisteredEvents.Count >= 1 && 
                _orderPlacedEvents.Count >= 1, 
                TimeSpan.FromSeconds(15));

            // Verify each consumer processed only its own event type
            lock (_locationCreatedEvents)
            {
                _locationCreatedEvents.Should().HaveCount(1);
                _locationCreatedEvents[0].Name.Should().Be(locationEvent.Name);
            }
            
            lock (_userRegisteredEvents)
            {
                _userRegisteredEvents.Should().HaveCount(1);
                _userRegisteredEvents[0].Username.Should().Be(userEvent.Username);
            }
            
            lock (_orderPlacedEvents)
            {
                _orderPlacedEvents.Should().HaveCount(1);
                _orderPlacedEvents[0].OrderNumber.Should().Be(orderEvent.OrderNumber);
            }
        }

        [Fact]
        public async Task MultipleEventsOfSameType_ShouldAllBeProcessed_ByRespectiveConsumer()
        {
            // Arrange - Clear previous events
            lock (_locationCreatedEvents) _locationCreatedEvents.Clear();
            lock (_userRegisteredEvents) _userRegisteredEvents.Clear();
            lock (_orderPlacedEvents) _orderPlacedEvents.Clear();
            
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
                    await _producer!.ProduceAsync(SHARED_TOPIC, 
                        new Message<string, string> 
                        { 
                            Key = nameof(LocationCreatedEvent),
                            Value = JsonSerializer.Serialize(locationEvents[i]) 
                        });
                }
                
                // Publish a user event
                if (i < userEvents.Count)
                {
                    await _producer!.ProduceAsync(SHARED_TOPIC, 
                        new Message<string, string> 
                        { 
                            Key = nameof(UserRegisteredEvent),
                            Value = JsonSerializer.Serialize(userEvents[i]) 
                        });
                }
                
                // Publish an order event
                if (i < orderEvents.Count)
                {
                    await _producer!.ProduceAsync(SHARED_TOPIC, 
                        new Message<string, string> 
                        { 
                            Key = nameof(OrderPlacedEvent),
                            Value = JsonSerializer.Serialize(orderEvents[i]) 
                        });
                }
            }

            // Assert - Wait for all events to be processed
            await WaitForConditionAsync(() => 
                _locationCreatedEvents.Count >= locationEvents.Count && 
                _userRegisteredEvents.Count >= userEvents.Count && 
                _orderPlacedEvents.Count >= orderEvents.Count, 
                TimeSpan.FromSeconds(15));

            // Verify each consumer processed all of its event types
            lock (_locationCreatedEvents)
            {
                _locationCreatedEvents.Should().HaveCount(locationEvents.Count);
                var locationNames = _locationCreatedEvents.Select(e => e.Name).OrderBy(n => n).ToList();
                locationNames.Should().BeEquivalentTo(locationEvents.Select(e => e.Name).OrderBy(n => n));
            }
            
            lock (_userRegisteredEvents)
            {
                _userRegisteredEvents.Should().HaveCount(userEvents.Count);
                var usernames = _userRegisteredEvents.Select(e => e.Username).OrderBy(u => u).ToList();
                usernames.Should().BeEquivalentTo(userEvents.Select(e => e.Username).OrderBy(u => u));
            }
            
            lock (_orderPlacedEvents)
            {
                _orderPlacedEvents.Should().HaveCount(orderEvents.Count);
                var orderNumbers = _orderPlacedEvents.Select(e => e.OrderNumber).OrderBy(o => o).ToList();
                orderNumbers.Should().BeEquivalentTo(orderEvents.Select(e => e.OrderNumber).OrderBy(o => o));
            }
        }
        
        [Fact]
        public async Task InvalidEventData_ShouldNotImpact_ProcessingOfValidEvents()
        {
            // Arrange - Clear previous events
            lock (_locationCreatedEvents) _locationCreatedEvents.Clear();
            lock (_userRegisteredEvents) _userRegisteredEvents.Clear();
            lock (_orderPlacedEvents) _orderPlacedEvents.Clear();
            
            // Send invalid JSON for each event type
            await _producer!.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(LocationCreatedEvent),
                    Value = "{ invalid json for location }" 
                });
                
            await _producer.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(UserRegisteredEvent),
                    Value = "not valid json" 
                });
                
            await _producer.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(OrderPlacedEvent),
                    Value = null 
                });
                
            // Wait a bit to ensure invalid messages were processed
            await Task.Delay(2000);
            
            // No events should have been processed
            lock (_locationCreatedEvents) _locationCreatedEvents.Should().BeEmpty();
            lock (_userRegisteredEvents) _userRegisteredEvents.Should().BeEmpty();
            lock (_orderPlacedEvents) _orderPlacedEvents.Should().BeEmpty();
            
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
            
            await _producer.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(LocationCreatedEvent),
                    Value = JsonSerializer.Serialize(validLocation) 
                });
                
            await _producer.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(UserRegisteredEvent),
                    Value = JsonSerializer.Serialize(validUser) 
                });
                
            await _producer.ProduceAsync(SHARED_TOPIC, 
                new Message<string, string> 
                { 
                    Key = nameof(OrderPlacedEvent),
                    Value = JsonSerializer.Serialize(validOrder) 
                });
                
            // Assert - Valid events should be processed
            await WaitForConditionAsync(() => 
                _locationCreatedEvents.Count >= 1 && 
                _userRegisteredEvents.Count >= 1 && 
                _orderPlacedEvents.Count >= 1, 
                TimeSpan.FromSeconds(10));
                
            // Verify each consumer processed its valid event
            lock (_locationCreatedEvents)
            {
                _locationCreatedEvents.Should().ContainSingle()
                    .Which.Name.Should().Be(validLocation.Name);
            }
            
            lock (_userRegisteredEvents)
            {
                _userRegisteredEvents.Should().ContainSingle()
                    .Which.Username.Should().Be(validUser.Username);
            }
            
            lock (_orderPlacedEvents)
            {
                _orderPlacedEvents.Should().ContainSingle()
                    .Which.OrderNumber.Should().Be(validOrder.OrderNumber);
            }
        }

        private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
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
    }
}