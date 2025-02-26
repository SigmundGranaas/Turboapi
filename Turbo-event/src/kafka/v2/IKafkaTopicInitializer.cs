using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turbo_event.kafka;

namespace Turboapi.Infrastructure.Kafka
{
    

    public class SimpleKafkaTopicInitializer : ITopicInitializer
    {
        private readonly KafkaSettings _settings;
        private readonly HashSet<string> _existingTopics = new();
        private readonly SemaphoreSlim _topicCreationLock = new(1, 1);
        private readonly ILogger<SimpleKafkaTopicInitializer> _logger;

        public SimpleKafkaTopicInitializer(
            IOptions<KafkaSettings> settings,
            ILogger<SimpleKafkaTopicInitializer> logger)
        {
            _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task EnsureTopicExists(string topicName)
        {
            if (string.IsNullOrWhiteSpace(topicName))
            {
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            }

            // Return immediately if we know topic exists
            if (_existingTopics.Contains(topicName))
            {
                return;
            }

            // Only allow one topic creation at a time
            await _topicCreationLock.WaitAsync();
            try
            {
                // Check again in case another thread created it while we were waiting
                if (_existingTopics.Contains(topicName))
                {
                    return;
                }

                using var adminClient = new AdminClientBuilder(
                    new AdminClientConfig { BootstrapServers = _settings.BootstrapServers }
                ).Build();

                try
                {
                    // Try to check if topic exists
                    var metadata = adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
                    if (metadata.Topics.Any(t => t.Topic == topicName))
                    {
                        _logger.LogDebug("Topic {TopicName} already exists", topicName);
                        _existingTopics.Add(topicName);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking if topic {TopicName} exists, will attempt to create it", topicName);
                }

                // Create the topic
                try
                {
                    _logger.LogInformation("Creating Kafka topic: {TopicName}", topicName);
                    
                    var topicSpecification = new TopicSpecification
                    {
                        Name = topicName,
                        ReplicationFactor = 1
                    };

                    await adminClient.CreateTopicsAsync(new[] { topicSpecification });
                    _logger.LogInformation("Created Kafka topic: {TopicName}", topicName);
                    _existingTopics.Add(topicName);
                }
                catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
                {
                    _logger.LogDebug("Topic {TopicName} already exists", topicName);
                    _existingTopics.Add(topicName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating Kafka topic: {TopicName}", topicName);
                    throw;
                }
            }
            finally
            {
                _topicCreationLock.Release();
            }
        }
    }
}