using Turboapi.infrastructure;

namespace Turboapi_geo.infrastructure;

using System;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Helper class to wait for Kafka message processing completion
/// </summary>
public class KafkaMessageWaiter
{
    private readonly string _bootstrapServers;
    private readonly string _topic;
    private readonly string _consumerGroup;
    
    public KafkaMessageWaiter(string bootstrapServers, string topic, string consumerGroup)
    {
        _bootstrapServers = bootstrapServers;
        _topic = topic;
        _consumerGroup = consumerGroup;
    }

    /// <summary>
    /// Waits until there are no more unprocessed messages in the topic for the consumer group
    /// </summary>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="pollInterval">Time between checks</param>
    /// <returns>True if all messages were processed, false if timeout occurred</returns>
    public async Task<bool> WaitForMessagesProcessed(TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        pollInterval ??= TimeSpan.FromMilliseconds(100);
        var startTime = DateTime.UtcNow;
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            if (await AllMessagesProcessed())
            {
                return true;
            }
            
            await Task.Delay(pollInterval.Value);
        }
        
        return false;
    }
    
    private async Task<bool> AllMessagesProcessed()
    {
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = _bootstrapServers
        };
        
        using var adminClient = new AdminClientBuilder(adminConfig).Build();
        
        // Get consumer group info
        var groupInfo = await adminClient.DescribeConsumerGroupsAsync(new[] { _consumerGroup });
        
        if (!groupInfo.ConsumerGroupDescriptions.Any() || 
            groupInfo.ConsumerGroupDescriptions[0].Members.Count == 0)
        {
            // No consumers in the group or group doesn't exist yet
            return false;
        }
        
        // Get information about topic partitions and their offsets
        var metadata = adminClient.GetMetadata(_topic, TimeSpan.FromSeconds(10));
        var topicPartitions = metadata.Topics
            .Where(t => t.Topic == _topic)
            .SelectMany(t => t.Partitions.Select(p => new TopicPartition(_topic, p.PartitionId)))
            .ToList();
        
        if (!topicPartitions.Any())
        {
            // Topic doesn't exist or has no partitions
            return false;
        }
        
        // Get partition offsets
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = Guid.NewGuid().ToString(), // Temporary group for metadata lookups
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };
        
        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        
        // Get the end offsets (latest)
        var endOffsets = new Dictionary<TopicPartition, WatermarkOffsets>();
        foreach (var tp in topicPartitions)
        {
            var watermarks = consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(10));
            endOffsets.Add(tp, watermarks);
        }
        
        // Get the committed offsets for our consumer group
        using var groupConsumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = _consumerGroup
        }).Build();
        
        var committedOffsets = topicPartitions
            .Select(tp => 
            {
                try
                {
                    return (tp, offset: groupConsumer.Committed(new[] { tp }, TimeSpan.FromSeconds(5)).First().Offset);
                }
                catch
                {
                    // If no offset is committed yet
                    return (tp, offset: new Offset(-1000)); // An invalid offset to indicate not committed
                }
            })
            .ToDictionary(x => x.tp, x => x.offset);
        
        // Check if all committed offsets are at or beyond the end offsets
        foreach (var entry in endOffsets)
        {
            var tp = entry.Key;
            var highOffset = entry.Value.High;
            
            if (!committedOffsets.TryGetValue(tp, out var committedOffset) || 
                committedOffset.Value < highOffset.Value - 1) // -1 because committed offset is the next message to consume
            {
                return false; // There are still messages to process
            }
        }
        
        return true; // All messages have been processed
    }
    
    /// <summary>
    /// Extension method to get a KafkaMessageWaiter from the service provider
    /// </summary>
    public static KafkaMessageWaiter FromServices(IServiceProvider services, string topic, string consumerGroup)
    {
        var kafkaSettings = services.GetRequiredService<KafkaSettings>();
        return new KafkaMessageWaiter(
            kafkaSettings.BootstrapServers,
            topic,
            consumerGroup
        );
    }
}