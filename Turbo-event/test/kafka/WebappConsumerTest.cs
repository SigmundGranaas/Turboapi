using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Testcontainers.Kafka;
using Turbo_event.kafka;
using Turboapi.Infrastructure.Kafka;
using Xunit;

namespace Turboapi.Tests
{
    public class WebApiStyleKafkaTests : IAsyncLifetime
    {
        private readonly KafkaContainer _kafka;
        private const string TOPIC_NAME = "webapi-test-events";
        private IProducer<string, string>? _producer;
        private IHost? _host;
        private readonly EventTracker _eventTracker = new();
        
        public WebApiStyleKafkaTests()
        {
            _kafka = new KafkaBuilder()
                .WithImage("confluentinc/cp-kafka:6.2.10")
                .WithPortBinding(9092, true)
                .Build();
        }
        
        public async Task InitializeAsync()
        {
            // Start Kafka container
            await _kafka.StartAsync();
            
            // Create Kafka producer
            _producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _kafka.GetBootstrapAddress()
            }).Build();
            
            // Create configuration
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Kafka:BootstrapServers"] = _kafka.GetBootstrapAddress(),
                    ["Kafka:AutoCreateTopics"] = "true",
                    ["Kafka:DefaultPartitions"] = "1",
                    ["Kafka:ReplicationFactor"] = "1",
                    ["Kafka:Topics:Test"] = TOPIC_NAME,
                    ["Kafka:ConsumerGroups:Test"] = "test-group"
                })
                .Build();
            
            // Create host with WebAPI-like DI registration
            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .ConfigureServices(services =>
                {
                    // Register event tracker
                    services.AddSingleton(_eventTracker);
                    
                    // Register Kafka infrastructure (similar to how it would be in Program.cs)
                    services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
                    services.AddSingleton<ITopicInitializer, SimpleKafkaTopicInitializer>();
                    services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();
                    
                    // Register event registry
                    var registry = new EventTypeRegistry();
                    registry.RegisterEventType<TestEvent>();
                    services.AddSingleton<EventJsonConverter>(new EventJsonConverter(registry));
                    
                    // Register event handlers
                    services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
                    
                    // Register consumer config
                    services.AddSingleton(new KafkaConsumerConfig<TestEvent>
                    {
                        Topic = TOPIC_NAME,
                        GroupId = "test-group"
                    });
                    
                    // Register Kafka consumer as hosted service using scoped handler pattern
                    services.AddHostedService<ScopedKafkaConsumerService<TestEvent>>();
                })
                .Build();
            
            // Start the host
            await _host.StartAsync();
        }
        
        public async Task DisposeAsync()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
            
            _producer?.Dispose();
            
            await _kafka.DisposeAsync();
        }
        
        [Fact]
        public async Task KafkaConsumer_RegisteredInWebApiStyle_ShouldProcessMessages()
        {
            // Arrange
            _eventTracker.Clear();
            
            var testEvent = new TestEvent 
            { 
                Id = Guid.NewGuid().ToString(),
                Data = "WebAPI-style Test Data" 
            };
            
            // Act - Send message to Kafka
            await _producer!.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> 
                {
                    Key = nameof(TestEvent),
                    Value = JsonSerializer.Serialize(testEvent)
                });
            
            // Assert - Wait for message to be processed
            await WaitForConditionAsync(() => _eventTracker.Count > 0, TimeSpan.FromSeconds(10));
            
            // Verify the event was processed
            _eventTracker.GetEvents().Should().ContainSingle()
                .Which.Data.Should().Be(testEvent.Data);
            
            // Send another message to verify continuous processing
            var secondEvent = new TestEvent 
            { 
                Id = Guid.NewGuid().ToString(),
                Data = "Second WebAPI-style Test Data" 
            };
            
            await _producer.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> 
                {
                    Key = nameof(TestEvent),
                    Value = JsonSerializer.Serialize(secondEvent)
                });
            
            // Wait for second message to be processed
            await WaitForConditionAsync(() => _eventTracker.Count > 1, TimeSpan.FromSeconds(10));
            
            // Verify both events were processed
            _eventTracker.GetEvents().Should().HaveCount(2);
            _eventTracker.GetEvents()[1].Data.Should().Be(secondEvent.Data);
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
        
        // Event class
        public record TestEvent : Event
        {
            public string Id { get; set; } = "";
            public string Data { get; set; } = "";
        }
        
        // Thread-safe event tracker
        public class EventTracker
        {
            private readonly List<TestEvent> _events = new();
            private readonly object _lock = new();
            
            public void Add(TestEvent @event)
            {
                lock (_lock)
                {
                    _events.Add(@event);
                }
            }
            
            public void Clear()
            {
                lock (_lock)
                {
                    _events.Clear();
                }
            }
            
            public int Count
            {
                get
                {
                    lock (_lock)
                    {
                        return _events.Count;
                    }
                }
            }
            
            public List<TestEvent> GetEvents()
            {
                lock (_lock)
                {
                    return _events.ToList();
                }
            }
        }
        
        // Event handler that adds events to the tracker
        public class TestEventHandler : IEventHandler<TestEvent>
        {
            private readonly EventTracker _eventTracker;
            private readonly ILogger<TestEventHandler> _logger;
            
            public TestEventHandler(EventTracker eventTracker, ILogger<TestEventHandler> logger)
            {
                _eventTracker = eventTracker;
                _logger = logger;
            }
            
            public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
            {
                _logger.LogInformation("Handling test event: {Id} - {Data}", @event.Id, @event.Data);
                _eventTracker.Add(@event);
                return Task.CompletedTask;
            }
        }
        
        // Wrapper service to handle scoped event handlers
        public class ScopedKafkaConsumerService<TEvent> : IHostedService where TEvent : Event
        {
            private readonly IServiceScopeFactory _scopeFactory;
            private readonly KafkaConsumerConfig<TEvent> _config;
            private readonly IOptions<KafkaSettings> _settings;
            private readonly ITopicInitializer _topicInitializer;
            private readonly IKafkaConsumerFactory _consumerFactory;
            private readonly JsonSerializerOptions _converter;
            private readonly ILogger<KafkaConsumer<TEvent>> _logger;
            private KafkaConsumer<TEvent>? _consumer;
            
            public ScopedKafkaConsumerService(
                IServiceScopeFactory scopeFactory,
                KafkaConsumerConfig<TEvent> config,
                IOptions<KafkaSettings> settings,
                ITopicInitializer topicInitializer,
                IKafkaConsumerFactory consumerFactory,
                JsonSerializerOptions converter,
                ILogger<Infrastructure.Kafka.KafkaConsumer<TEvent>> logger)
            {
                _scopeFactory = scopeFactory;
                _config = config;
                _settings = settings;
                _topicInitializer = topicInitializer;
                _consumerFactory = consumerFactory;
                _converter = converter;
                _logger = logger;
            }
            
            public Task StartAsync(CancellationToken cancellationToken)
            {
                // Create a wrapper handler that creates a scope for each event
                var handler = new ScopedEventHandlerWrapper<TEvent>(_scopeFactory);
                
                // Create the actual consumer
                _consumer = new Infrastructure.Kafka.KafkaConsumer<TEvent>(
                    handler,
                    _config,
                    _settings,
                    _topicInitializer,
                    _consumerFactory,
                    _converter,
                    _logger);
                
                return _consumer.StartAsync(cancellationToken);
            }
            
            public async Task StopAsync(CancellationToken cancellationToken)
            {
                if (_consumer != null)
                {
                    await _consumer.StopAsync(cancellationToken);
                    (_consumer as IDisposable)?.Dispose();
                }
            }
        }
        
        // Wrapper for scoped event handlers
        public class ScopedEventHandlerWrapper<TEvent> : IEventHandler<TEvent> where TEvent : Event
        {
            private readonly IServiceScopeFactory _scopeFactory;
            
            public ScopedEventHandlerWrapper(IServiceScopeFactory scopeFactory)
            {
                _scopeFactory = scopeFactory;
            }
            
            public async Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IEventHandler<TEvent>>();
                await handler.HandleAsync(@event, cancellationToken);
            }
        }
    }
}