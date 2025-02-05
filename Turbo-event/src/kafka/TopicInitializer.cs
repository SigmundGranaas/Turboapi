using Microsoft.Extensions.Logging;
using Turbo_event.kafka;

namespace Turbo_event.test.kafka;

using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;

public class KafkaTopicInitializer : ITopicInitializer
{
    private readonly IAdminClient _adminClient;
    private readonly ILogger<KafkaTopicInitializer> _logger;
    private readonly ConcurrentDictionary<string, bool> _initializedTopics;
    private readonly SemaphoreSlim _initLock;
    private readonly int _partitions;
    private readonly short _replicationFactor;

    public KafkaTopicInitializer(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaTopicInitializer> logger,
        int partitions = 1,
        short replicationFactor = 1)
    {
        ArgumentNullException.ThrowIfNull(settings?.Value);

        var config = new AdminClientConfig
        {
            BootstrapServers = settings.Value.BootstrapServers
        };

        _adminClient = new AdminClientBuilder(config).Build();
        _logger = logger;
        _initializedTopics = new ConcurrentDictionary<string, bool>();
        _initLock = new SemaphoreSlim(1, 1);
        _partitions = partitions;
        _replicationFactor = replicationFactor;
    }

    public async Task EnsureTopicExists(string topic)
    {
        // Fast path - if we've already initialized this topic
        if (_initializedTopics.TryGetValue(topic, out var exists) && exists)
        {
            return;
        }

        try
        {
            await _initLock.WaitAsync();

            // Check again after acquiring lock
            if (_initializedTopics.TryGetValue(topic, out exists) && exists)
            {
                return;
            }

            var metadata = _adminClient.GetMetadata(TimeSpan.FromSeconds(10));
            var topicExists = metadata.Topics.Any(t => t.Topic == topic);

            if (!topicExists)
            {
                try
                {
                    _logger.LogInformation("Creating topic: {Topic}", topic);
                    
                    await _adminClient.CreateTopicsAsync(new []
                    {
                        new TopicSpecification
                        {
                            Name = topic,
                            NumPartitions = _partitions,
                            ReplicationFactor = _replicationFactor
                        }
                    });
                    
                    _logger.LogInformation("Successfully created topic: {Topic}", topic);
                }
                catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
                {
                    _logger.LogInformation("Topic already exists: {Topic}", topic);
                }
            }

            _initializedTopics.TryAdd(topic, true);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        _adminClient?.Dispose();
        _initLock?.Dispose();
    }
}