using System.Collections.Concurrent;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using Turboapi.infrastructure;

namespace Turboapi_geo.infrastructure;

public interface IKafkaTopicInitializer
{
    Task EnsureTopicExists(string topic, int partitions = 1, short replicationFactor = 1);
}

public class KafkaTopicInitializer : IKafkaTopicInitializer
{
    private readonly IAdminClient _adminClient;
    private readonly ILogger<KafkaTopicInitializer>? _logger;
    private readonly ConcurrentDictionary<string, bool> _initializedTopics;
    private readonly SemaphoreSlim _initLock;

    public KafkaTopicInitializer(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaTopicInitializer> logger)
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
    }
    
    public KafkaTopicInitializer(
        IOptions<KafkaSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings?.Value);

        var config = new AdminClientConfig
        {
            BootstrapServers = settings.Value.BootstrapServers
        };

        _adminClient = new AdminClientBuilder(config).Build();
        _initializedTopics = new ConcurrentDictionary<string, bool>();
        _initLock = new SemaphoreSlim(1, 1);
    }

    public async Task EnsureTopicExists(string topic, int partitions = 1, short replicationFactor = 1)
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
                    _logger?.LogInformation("Creating topic: {Topic}", topic);
                    
                    await _adminClient.CreateTopicsAsync(new TopicSpecification[]
                    {
                        new TopicSpecification
                        {
                            Name = topic,
                            NumPartitions = partitions,
                            ReplicationFactor = replicationFactor
                        }
                    });
                    
                    _logger?.LogInformation("Successfully created topic: {Topic}", topic);
                }
                catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
                {
                    _logger?.LogInformation("Topic already exists: {Topic}", topic);
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