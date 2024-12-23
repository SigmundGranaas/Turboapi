using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using Turboapi.events;

namespace Turboapi.infrastructure;

public interface IEventPublisher
{
    Task PublishAsync<T>(T @event) where T : UserAccountEvent;
}

public class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly string _bootstrapServers;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private bool _topicCreated;

    public KafkaEventPublisher(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventPublisher> logger)
    {
        if (settings?.Value == null)
            throw new ArgumentNullException(nameof(settings), "Kafka settings cannot be null");
            
        if (string.IsNullOrEmpty(settings.Value.BootstrapServers))
            throw new ArgumentException("BootstrapServers must be configured in Kafka settings");
            
        if (string.IsNullOrEmpty(settings.Value.UserAccountsTopic))
            throw new ArgumentException("UserAccountsTopic must be configured in Kafka settings");

        _bootstrapServers = settings.Value.BootstrapServers;
        _topic = settings.Value.UserAccountsTopic;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = new ProducerConfig
        {
            BootstrapServers = _bootstrapServers,
            MessageTimeoutMs = 5000,
            RequestTimeoutMs = 5000,
            MessageSendMaxRetries = 3,
            EnableDeliveryReports = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
        _logger.LogInformation("Initialized Kafka publisher with bootstrap servers: {Servers} and topic: {Topic}", 
            _bootstrapServers, _topic);
    }

    private async Task EnsureTopicExistsAsync()
    {
        if (_topicCreated) return;

        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _bootstrapServers
        }).Build();

        try
        {
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
            if (metadata.Topics.Any(t => t.Topic == _topic))
            {
                _topicCreated = true;
                return;
            }

            await adminClient.CreateTopicsAsync(new TopicSpecification[] {
                new TopicSpecification 
                { 
                    Name = _topic,
                    ReplicationFactor = 1,
                    NumPartitions = 1
                }
            });
            
            _topicCreated = true;
            _logger.LogInformation("Created Kafka topic: {Topic}", _topic);
        }
        catch (CreateTopicsException ex)
        {
            if (ex.Results.First().Error.Code == ErrorCode.TopicAlreadyExists)
            {
                _topicCreated = true;
                return;
            }
            throw;
        }
    }

    public async Task PublishAsync<T>(T @event) where T : UserAccountEvent
    {
        try 
        {
            await EnsureTopicExistsAsync();

            var message = new Message<string, string>
            {
                Key = @event.AccountId.ToString(),
                Value = JsonSerializer.Serialize(@event)
            };

            await _producer.ProduceAsync(_topic, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to Kafka topic {Topic}", _topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}