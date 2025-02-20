using Turbo_event.kafka;
using Turbo_event.test.kafka;

namespace EventHandling.Kafka.Tests;

using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using Xunit;


public class KafkaConsumerTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka;
    private const string TOPIC_NAME = "test-events";
    private KafkaConsumer<TestEvent>? _consumer;
    private IProducer<string, string>? _producer;
    private readonly List<TestEvent> _processedEvents;
    private IServiceProvider? _serviceProvider;
    
    public KafkaConsumerTests()
    {
        _processedEvents = new List<TestEvent>();

        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:6.2.10")
            .WithPortBinding(9092, true)
            .Build();
    }

    
    public class TestEvent
    {
        public string Id { get; set; } = "";
        public string Data { get; set; } = "";
    }
    
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

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        
        services.AddSingleton(Options.Create(new KafkaSettings
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            Topic = TOPIC_NAME,
            ConsumerGroupId = "test-group"
        }));

        services.AddSingleton<ITopicInitializer, KafkaTopicInitializer>();
        services.AddSingleton<JsonSerializerOptions>();
        services.AddSingleton<KafkaMessageProcessor<TestEvent>>();
        services.AddScoped<KafkaMessageProcessor<TestEvent>>(sp => new KafkaMessageProcessor<TestEvent>(sp.GetRequiredService<IServiceScopeFactory>(), new JsonSerializerOptions(), _serviceProvider.GetRequiredService<ILogger<KafkaMessageProcessor<TestEvent>>>()));

        services.AddScoped<IEventHandler<TestEvent>>(sp => new TestEventHandler(_processedEvents));

        _serviceProvider = services.BuildServiceProvider();

        _consumer = new KafkaConsumer<TestEvent>(
            _serviceProvider.GetRequiredService<ITopicInitializer>(),
            _serviceProvider.GetRequiredService<IOptions<KafkaSettings>>(),
            _serviceProvider.GetRequiredService<KafkaMessageProcessor<TestEvent>>(),
            _serviceProvider.GetRequiredService<ILogger<KafkaConsumer<TestEvent>>>());

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        }).Build();
    }

    public async Task DisposeAsync()
    {
        await _consumer!.StopAsync(CancellationToken.None);
        _consumer.Dispose();
        _producer?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        await _kafka.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_ShouldProcessMessages_WhenProduced()
    {
        // Arrange
        var testEvent = new TestEvent { Id = "1", Data = "Test Data" };
        var serializedEvent = JsonSerializer.Serialize(testEvent);
        await _consumer!. StartAsync(CancellationToken.None);

        // Act
        await _producer!.ProduceAsync(TOPIC_NAME, 
            new Message<string, string> { Value = serializedEvent });

        // Assert
        await WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
        _processedEvents.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(testEvent);
    }

    [Fact]
    public async Task Consumer_ShouldHandleMultipleMessages_InOrder()
    {
        // Arrange
        var events = Enumerable.Range(1, 5)
            .Select(i => new TestEvent { Id = i.ToString(), Data = $"Data {i}" })
            .ToList();
            
        await _consumer!.StartAsync(CancellationToken.None);

        // Act
        foreach (var @event in events)
        {
            await _producer!.ProduceAsync(TOPIC_NAME, 
                new Message<string, string> { Value = JsonSerializer.Serialize(@event) });
        }

        // Assert
        await WaitForConditionAsync(() => _processedEvents.Count == 5, TimeSpan.FromSeconds(10));
        _processedEvents.Should().BeEquivalentTo(events, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Consumer_ShouldHandleInvalidMessages_Gracefully()
    {
        // Arrange
        await _consumer!.StartAsync(CancellationToken.None);

        // Act
        await _producer!.ProduceAsync(TOPIC_NAME, 
            new Message<string, string> { Value = "invalid json" });
        await _producer.ProduceAsync(TOPIC_NAME, 
            new Message<string, string> { Value = null });

        var validEvent = new TestEvent { Id = "1", Data = "Valid" };
        await _producer.ProduceAsync(TOPIC_NAME, 
            new Message<string, string> { Value = JsonSerializer.Serialize(validEvent) });

        // Assert
        await WaitForConditionAsync(() => _processedEvents.Count == 1, TimeSpan.FromSeconds(10));
        _processedEvents.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(validEvent);
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