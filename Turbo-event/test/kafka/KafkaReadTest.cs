
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using Turbo_event.kafka;
using Turbo_event.util;
using Xunit;


public class KafkaEventReaderTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka;
    private const string TOPIC_NAME = "test-events";
    private KafkaEventReader? _reader;
    private IProducer<string, string>? _producer;
    private ILogger<KafkaEventReader>? _logger;
    private KafkaSettings _settings;
    private JsonSerializerOptions? _jsonOptions;

    public KafkaEventReaderTests()
    {
        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:6.2.10")
            .WithName($"kafka-test-{Guid.NewGuid()}")
            .WithPortBinding(9092, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _kafka.StartAsync();

        _settings = new KafkaSettings
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            Topic = TOPIC_NAME
        };
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<KafkaEventReader>();

        var registry = new EventTypeRegistry();
        registry.RegisterEventType<TestEvent>(nameof(TestEvent));
        
        var converter = new EventJsonConverter(registry);
         _jsonOptions = JsonConfig.CreateDefault(converter);
        
        _reader = new KafkaEventReader(_jsonOptions,Options.Create(_settings), _logger);

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        };

        _producer = new ProducerBuilder<string, string>(producerConfig).Build();

        // Create topic
        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        }).Build();

        await adminClient.CreateTopicsAsync(new[]
        {
            new TopicSpecification
            {
                Name = TOPIC_NAME,
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        });
    }

    public async Task DisposeAsync()
    {
        _reader?.Dispose();
        _producer?.Dispose();
        await _kafka.DisposeAsync();
    }

    private async Task ProduceEvents(IEnumerable<Event> events)
    {
        foreach (var @event in events)
        {
            await _producer!.ProduceAsync(TOPIC_NAME, new Message<string, string>
            {
                Key = @event.GetType().FullName,
                Value = JsonSerializer.Serialize(@event, _jsonOptions)
            });
        }
    }

    [Fact]
    public async Task GetEventsAfter_ShouldReturnEventsAfterSpecifiedPosition()
    {
        // Arrange
        var events = new[]
        {
            new TestEvent {  Data = "Event 1" },
            new TestEvent {  Data = "Event 2" },
            new TestEvent {  Data = "Event 3" }
        };
        await ProduceEvents(events);

        // Act
        var result = await _reader!.GetEventsAfter(1);

        // Assert
        result.Should().HaveCount(2);
        result.Select(e => ((TestEvent)e).Data)
            .Should().BeEquivalentTo(new[] { "Event 2", "Event 3" });
    }
    

    [Fact]
    public async Task GetEventsAfter_WithNoEvents_ShouldReturnEmptyCollection()
    {
        // Act
        var result = await _reader!.GetEventsAfter(0);

        // Assert
        result.Should().BeEmpty();
    }
    
    private record TestEvent : Event
    {
        public string Data { get; set; }
    }
}