using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
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
    private readonly ActivitySource _activitySource;

    public KafkaEventPublisher(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventPublisher> logger)
    {
        _activitySource = new ActivitySource("KafkaPublisher");
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
        using var activity = _activitySource.StartActivity(
            $"Publish {typeof(T).Name}", 
            ActivityKind.Producer);

        try 
        {
            await EnsureTopicExistsAsync();

            var headers = new Headers();
        
            // Inject trace context into message headers
            if (activity != null)
            {
                var propagationContext = new PropagationContext(activity.Context, Baggage.Current);
                Propagators.DefaultTextMapPropagator.Inject(propagationContext, headers, 
                    (headers, key, value) => headers.Add(key, Encoding.UTF8.GetBytes(value)));

                // Add useful tags for tracing
                activity.SetTag("messaging.system", "kafka");
                activity.SetTag("messaging.destination", _topic);
                activity.SetTag("messaging.destination_kind", "topic");
                activity.SetTag("messaging.account_id", @event.AccountId);  // Using AccountId instead of EventId
                activity.SetTag("messaging.event_type", typeof(T).Name);   // Adding event type for better tracing
                activity.SetTag("messaging.operation", "publish");
            }

            var message = new Message<string, string>
            {
                Key = @event.AccountId.ToString(),
                Value = JsonSerializer.Serialize(@event),
                Headers = headers
            };

            var result = await _producer.ProduceAsync(_topic, message);
            activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
            activity?.SetTag("messaging.kafka.offset", result.Offset.Value);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to publish message to Kafka topic {Topic}", _topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}