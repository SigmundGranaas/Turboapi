using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using Turbo_event.kafka;
using Turbo_event.test.kafka;
using Turbo_event.util;
using Xunit;
using KafkaTopicInitializer = Turbo_event.test.kafka.KafkaTopicInitializer;

namespace EventHandling.Kafka.Tests;

public class KafkaEventStoreWriterTests : IAsyncLifetime
{
    private readonly KafkaContainer _kafka;
    private const string TOPIC_NAME = "test-events";
    private KafkaEventStoreWriter? _writer;
    private ITopicInitializer? _topicInitializer;
    private IConsumer<string, string>? _consumer;
    private ILogger<KafkaEventStoreWriter>? _writerLogger;
    private ILogger<KafkaTopicInitializer>? _initializerLogger;
    private IOptions<KafkaSettings> _kafkaSettings;

    public KafkaEventStoreWriterTests()
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

        var settings = Options.Create(new KafkaSettings
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            Topic = TOPIC_NAME
        });
        
        _kafkaSettings = settings;
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _writerLogger = loggerFactory.CreateLogger<KafkaEventStoreWriter>();
        _initializerLogger = loggerFactory.CreateLogger<KafkaTopicInitializer>();

        _topicInitializer = new KafkaTopicInitializer(settings, _initializerLogger);

        var registry = new EventTypeRegistry();
        registry.RegisterEventType<TestCreated>(nameof(TestCreated));
        
        var converter = new EventJsonConverter(registry);
        JsonSerializerOptions opts = JsonConfig.CreateDefault(converter);
        _writer = new KafkaEventStoreWriter(_topicInitializer, new TestCreatedTopicResolver(), opts, settings, _writerLogger);

        // Setup consumer for verification
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        _consumer.Subscribe(TOPIC_NAME);
    }

    public async Task DisposeAsync()
    {
        _writer?.Dispose();
        _consumer?.Dispose();
        (_topicInitializer as IDisposable)?.Dispose();
        await _kafka.DisposeAsync();
    }

    private async Task<List<ConsumeResult<string, string>>> ConsumeMessages(
        int expectedCount, 
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var messages = new List<ConsumeResult<string, string>>();
        var stopwatch = Stopwatch.StartNew();

        while (messages.Count < expectedCount && stopwatch.Elapsed < timeout)
        {
            var result = _consumer!.Consume(TimeSpan.FromMilliseconds(100));
            if (result != null)
            {
                messages.Add(result);
            }
        }

        if (messages.Count < expectedCount)
        {
            throw new TimeoutException(
                $"Expected {expectedCount} messages but got {messages.Count} after {timeout.Value.TotalSeconds} seconds");
        }

        return messages;
    }

    public record TestCreated : Event
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task AppendEvents_ShouldCreateTopic_AndWriteEvents()
    {
        // Arrange
        var events = new Event[]
        {
            new TestCreated { Name = "Test Event 1" },
            new TestCreated { Name = "Test Event 2" }
        };

        // Act
        await _writer!.AppendEvents(events);

        // Assert
        var messages = await ConsumeMessages(2);
        messages.Should().HaveCount(2);
        messages.Should().AllSatisfy(m =>
        {
            m.Topic.Should().Be(TOPIC_NAME);
            m.Message.Key.Should().Be(nameof(TestCreated));
            m.Message.Value.Should().Contain("Test Event");
        });

        // Verify topic was created with correct settings
        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        }).Build();

        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
        var topicMetadata = metadata.Topics.Single(t => t.Topic == TOPIC_NAME);
        topicMetadata.Partitions.Count.Should().Be(1);
    }

    [Fact]
    public async Task AppendEvents_ShouldReuseExistingTopic()
    {
        // Arrange
        var firstEvent = new TestCreated { Name = "First Event" };
        await _writer!.AppendEvents(new[] { firstEvent });

        // Act
        var secondEvent = new TestCreated { Name = "Second Event" };
        await _writer.AppendEvents(new[] { secondEvent });

        // Assert
        var messages = await ConsumeMessages(2);
        messages.Should().HaveCount(2);
        messages[0].Message.Value.Should().Contain("First Event");
        messages[1].Message.Value.Should().Contain("Second Event");
    }

    [Fact]
    public async Task AppendEvents_WithCustomPartitions_ShouldCreateTopicCorrectly()
    {
        // Arrange
        const int customPartitions = 3;
        var init = new KafkaTopicInitializer(_kafkaSettings, _initializerLogger, customPartitions );
       await init.EnsureTopicExists(TOPIC_NAME);
        var events = new Event[]
        {
            new TestCreated { Name = "Test Event 1" },
            new TestCreated { Name = "Test Event 2" }
        };

        // Act
        await _writer!.AppendEvents(events);

        // Assert
        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        }).Build();

        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
        var topicMetadata = metadata.Topics.Single(t => t.Topic == TOPIC_NAME);
        topicMetadata.Partitions.Count.Should().Be(customPartitions);

        var messages = await ConsumeMessages(2);
        messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task AppendEvents_WhenKafkaIsDown_ShouldThrowException()
    {
        // Arrange
        var events = new Event[] { new TestCreated { Name = "Test Event" } };
        await _kafka.StopAsync();

        // Act & Assert
        await Assert.ThrowsAsync<KafkaException>(() => 
            _writer!.AppendEvents(events));
    }

    [Fact]
    public async Task AppendEvents_WithConcurrentWrites_ShouldHandleTopicInitializationCorrectly()
    {
        // Arrange
        var eventBatches = Enumerable.Range(0, 5)
            .Select(i => new TestCreated { Name = $"Event Batch {i}" })
            .Select(e => new[] { e })
            .ToArray();

        // Act
        var tasks = eventBatches
            .Select(batch => _writer!.AppendEvents(batch))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var messages = await ConsumeMessages(5);
        messages.Should().HaveCount(5);
        messages.Select(m => m.Message.Value)
            .Should().Contain(v => v.Contains("Event Batch"));
    }
    
    private class TestCreatedTopicResolver: IEventTopicResolver {
        public string ResolveTopicFor(Event e)
        {
            return TOPIC_NAME;
        }
    }
}