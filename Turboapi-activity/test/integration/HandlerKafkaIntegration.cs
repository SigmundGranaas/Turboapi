using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using Turbo_event.kafka;
using Turbo_event.test.kafka;
using Turbo_event.util;
using Turboauth_activity.domain;
using Turboauth_activity.domain.command;
using Turboauth_activity.domain.events;
using Turboauth_activity.domain.handler;
using Xunit;
using Activity = Turboauth_activity.domain.Activity;

namespace Turboauth_activity.test.integration;

public class HandlerKafkaIntegration: IAsyncLifetime
{
    private const string TOPIC_NAME = "activities";
    private readonly KafkaContainer _kafka;
    private IOptions<KafkaSettings> _kafkaSettings;
    private KafkaEventStoreWriter? _writer;
    private ITopicInitializer? _topicInitializer;
    private IConsumer<string, string>? _consumer;
    
    public HandlerKafkaIntegration()
    {
        // Setup Kafka
        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:6.2.10")
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
        var registry = new EventTypeRegistry();
        registry.RegisterEventType<ActivityCreated>(nameof(ActivityCreated));
        registry.RegisterEventType<ActivityPositionCreated>(nameof(ActivityPositionCreated));
        registry.RegisterEventType<ActivityDeleted>(nameof(ActivityDeleted));
        registry.RegisterEventType<ActivityUpdated>(nameof(ActivityUpdated));

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var writerLogger = loggerFactory.CreateLogger<KafkaEventStoreWriter>();

        var initializerLogger = loggerFactory.CreateLogger<KafkaTopicInitializer>();

        _topicInitializer = new KafkaTopicInitializer(settings, initializerLogger);

        _writer = new KafkaEventStoreWriter(_topicInitializer, new ActivityEventTopicResolver(), settings, writerLogger);
        
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


    [Fact]
    public async Task publishCreateActivityEvents()
    {
        var handler = new CreateActivityHandler(_writer);
        var command = new CreateActivityCommand()
        {
            OwnerId = new Guid("00000000-0000-0000-0000-000000000001"),
            Position = new Position{Latitude = 34, Longitude = 38},
            Name = "activity-events",
            Description = "activity-events",
            Icon = "Icon"
        };

        var result = await handler.Handle(command);
        var messages = await ConsumeMessages(1);
        
        messages.Should().HaveCount(1);
        messages[0].Message.Value.Should().Contain(result.ToString());
    }
    
    [Fact]
    public async Task publishDeleteActivityEvents()
    {
        var name = "name";
        var description = "email";
        var icon = "icon";
        Guid owner = Guid.NewGuid();
        var pos = new Position
        {
            Latitude = 47.789,
            Longitude = -122.451
        };
        
        var activity = Activity.Create(owner, pos, name, description, icon);
        var dict = new Dictionary<Guid, Activity>();
        dict.Add(activity.Id, activity);
        var repo = new InMemoryActivityReadModel(dict);
        
        var handler = new DeleteActivityHandler(_writer, repo);

        var command = new DeleteActivityCommand { ActivityID = activity.Id, UserID = owner };
        
        var result = await handler.Handle(command);
        var messages = await ConsumeMessages(1);
        
        messages.Should().HaveCount(1);
        messages[0].Message.Value.Should().Contain(result.ToString());
    }
}