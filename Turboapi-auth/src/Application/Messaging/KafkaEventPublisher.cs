// src/Infrastructure/Messaging/KafkaEventPublisher.cs
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Turboapi.Application.Interfaces;
using Turboapi.Domain.Events;

namespace Turboapi.Infrastructure.Messaging
{
    public class KafkaEventPublisher : IEventPublisher, IDisposable
    {
        private readonly IProducer<string, string> _producer;
        private readonly KafkaSettings _kafkaSettings;
        private readonly ILogger<KafkaEventPublisher> _logger;
        private bool _topicCreated;
        private readonly ActivitySource _activitySource;
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public KafkaEventPublisher(IOptions<KafkaSettings> settings, ILogger<KafkaEventPublisher> logger)
        {
            _kafkaSettings = settings?.Value ?? throw new ArgumentNullException(nameof(settings), "Kafka settings cannot be null.");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activitySource = new ActivitySource(GetType().FullName ?? "KafkaEventPublisher");

            if (string.IsNullOrEmpty(_kafkaSettings.BootstrapServers))
                throw new ArgumentException("BootstrapServers must be configured in Kafka settings.", nameof(settings));
            if (string.IsNullOrEmpty(_kafkaSettings.UserAccountsTopic))
                throw new ArgumentException("UserAccountsTopic must be configured in Kafka settings.", nameof(settings));

            var config = new ProducerConfig
            {
                BootstrapServers = _kafkaSettings.BootstrapServers,
                MessageTimeoutMs = _kafkaSettings.MessageTimeoutMs,
                RequestTimeoutMs = _kafkaSettings.RequestTimeoutMs,
                MessageSendMaxRetries = _kafkaSettings.MessageSendMaxRetries,
                EnableDeliveryReports = true // Required for ProduceAsync to await acknowledgment
            };

            // Add SASL/SSL configuration if provided
            // Example:
            // if (!string.IsNullOrEmpty(_kafkaSettings.SecurityProtocol)) config.SecurityProtocol = Enum.Parse<SecurityProtocol>(_kafkaSettings.SecurityProtocol, true);
            // if (!string.IsNullOrEmpty(_kafkaSettings.SaslMechanism)) config.SaslMechanism = Enum.Parse<SaslMechanism>(_kafkaSettings.SaslMechanism, true);
            // if (!string.IsNullOrEmpty(_kafkaSettings.SaslUsername)) config.SaslUsername = _kafkaSettings.SaslUsername;
            // if (!string.IsNullOrEmpty(_kafkaSettings.SaslPassword)) config.SaslPassword = _kafkaSettings.SaslPassword;
            // if (!string.IsNullOrEmpty(_kafkaSettings.SslCaLocation)) config.SslCaLocation = _kafkaSettings.SslCaLocation;

            _producer = new ProducerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka producer error: {Reason}. IsFatal: {IsFatal}", e.Reason, e.IsFatal))
                .SetLogHandler((_, m) => _logger.LogDebug("Kafka producer log: [{Level}] {Name} - {Message}", m.Level.ToString(), m.Name, m.Message))
                .Build();

            _logger.LogInformation("Initialized Kafka publisher with bootstrap servers: {Servers} and topic: {Topic}",
                _kafkaSettings.BootstrapServers, _kafkaSettings.UserAccountsTopic);
        }

        private async Task EnsureTopicExistsAsync()
        {
            if (_topicCreated) return;

            using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _kafkaSettings.BootstrapServers }).Build();
            try
            {
                var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
                if (metadata.Topics.Any(t => t.Topic == _kafkaSettings.UserAccountsTopic))
                {
                    _topicCreated = true;
                    _logger.LogInformation("Kafka topic {Topic} already exists.", _kafkaSettings.UserAccountsTopic);
                    return;
                }

                await adminClient.CreateTopicsAsync(new TopicSpecification[]
                {
                    new()
                    {
                        Name = _kafkaSettings.UserAccountsTopic,
                        ReplicationFactor = 1, // Suitable for single-broker dev setup; adjust for prod
                        NumPartitions = 1      // Adjust as needed
                    }
                });
                _topicCreated = true;
                _logger.LogInformation("Created Kafka topic: {Topic}", _kafkaSettings.UserAccountsTopic);
            }
            catch (CreateTopicsException ex)
            {
                if (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
                {
                    _topicCreated = true;
                    _logger.LogInformation("Kafka topic {Topic} confirmed to exist after CreateTopicsException (already exists).", _kafkaSettings.UserAccountsTopic);
                }
                else
                {
                    _logger.LogError(ex, "Error creating Kafka topic {Topic}", _kafkaSettings.UserAccountsTopic);
                    throw; // Re-throw if it's not a "TopicAlreadyExists" error for all specified topics
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Unexpected error during Kafka topic creation/validation for {Topic}", _kafkaSettings.UserAccountsTopic);
                 throw;
            }
        }

        public async Task PublishAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
        {
            if (domainEvent == null) throw new ArgumentNullException(nameof(domainEvent));

            using var activity = _activitySource.StartActivity($"Publish {typeof(TEvent).Name}", ActivityKind.Producer);
            try
            {
                await EnsureTopicExistsAsync();

                var headers = new Headers();
                if (activity != null)
                {
                    var propagationContext = new PropagationContext(activity.Context, Baggage.Current);
                    Propagators.DefaultTextMapPropagator.Inject(propagationContext, headers,
                        (carrier, key, value) => carrier.Add(key, Encoding.UTF8.GetBytes(value)));

                    activity.SetTag("messaging.system", "kafka");
                    activity.SetTag("messaging.destination", _kafkaSettings.UserAccountsTopic);
                    activity.SetTag("messaging.destination_kind", "topic");
                    activity.SetTag("messaging.event_type", typeof(TEvent).FullName); // Use full name for clarity
                    activity.SetTag("messaging.operation", "publish");

                    // Try to get AccountId if the event has it
                    if (domainEvent is IAccountAssociatedEvent accountEvent)
                    {
                         activity.SetTag("messaging.account_id", accountEvent.AccountId.ToString());
                    }
                    // Try to get a general EventId if available
                    // if (domainEvent is IEventWithId eventWithId)
                    // {
                    //     activity.SetTag("messaging.message_id", eventWithId.EventId.ToString());
                    // }
                }
                
                string eventKey = Guid.NewGuid().ToString(); // Default key
                if (domainEvent is IAccountAssociatedEvent keyedEvent)
                {
                    eventKey = keyedEvent.AccountId.ToString(); // Use AccountId as key if available
                }

                var messageValue = JsonSerializer.Serialize(domainEvent, typeof(TEvent), _jsonSerializerOptions);
                var message = new Message<string, string>
                {
                    Key = eventKey,
                    Value = messageValue,
                    Headers = headers
                };

                var deliveryResult = await _producer.ProduceAsync(_kafkaSettings.UserAccountsTopic, message);

                if (deliveryResult.Status == PersistenceStatus.NotPersisted || deliveryResult.Status == PersistenceStatus.PossiblyPersisted)
                {
                     _logger.LogWarning("Kafka message to topic {Topic} was not persisted or possibly persisted. Status: {Status}", _kafkaSettings.UserAccountsTopic, deliveryResult.Status);
                }
                else
                {
                    _logger.LogInformation("Successfully published event {EventType} to Kafka topic {Topic}, partition {Partition}, offset {Offset}",
                        typeof(TEvent).Name, deliveryResult.Topic, deliveryResult.Partition, deliveryResult.Offset);
                }

                activity?.SetTag("messaging.kafka.partition", deliveryResult.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", deliveryResult.Offset.Value);
                activity?.SetStatus(ActivityStatusCode.Ok);

            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Kafka produce error for event {EventType} to topic {Topic}: {ErrorReason}",
                    typeof(TEvent).Name, _kafkaSettings.UserAccountsTopic, ex.Error.Reason);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw; // Re-throw to indicate publishing failure
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish event {EventType} to Kafka topic {Topic}",
                    typeof(TEvent).Name, _kafkaSettings.UserAccountsTopic);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw; // Re-throw to indicate publishing failure
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Flush any outstanding messages
                _producer?.Flush(TimeSpan.FromSeconds(10));
                _producer?.Dispose();
                _activitySource?.Dispose();
            }
        }
    }

    // Optional: Helper interface to standardize getting AccountId from events for keying/tracing
    public interface IAccountAssociatedEvent
    {
        Guid AccountId { get; }
    }
}