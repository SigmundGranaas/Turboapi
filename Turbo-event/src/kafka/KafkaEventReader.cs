using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Context.Propagation;

namespace Turbo_event.kafka;

public class KafkaEventReader : IEventStoreReader, IDisposable 
{
    private readonly IConsumer<string, string> _consumer;
    private readonly string _topic;
    private readonly ILogger<KafkaEventReader> _logger;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly Counter<long> _eventReadCounter;
    private readonly Histogram<double> _readLatencyHistogram;
    private readonly Counter<long> _deserializationErrorCounter;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly SemaphoreSlim _consumeLock = new(1, 1);

    public KafkaEventReader(
        JsonSerializerOptions jsonSerializerOptions,
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventReader> logger)
    {
        _jsonSerializerOptions = jsonSerializerOptions;
        _logger = logger;
        
        _activitySource = new ActivitySource("KafkaEventReader");
        _meter = new Meter("KafkaEventReader");
        
        _eventReadCounter = _meter.CreateCounter<long>(
            "event_store_reads_total",
            description: "Total number of events read from the store");
            
        _readLatencyHistogram = _meter.CreateHistogram<double>(
            "event_store_read_duration_ms",
            unit: "ms",
            description: "Time taken to read events from the store");
            
        _deserializationErrorCounter = _meter.CreateCounter<long>(
            "event_store_deserialization_errors_total",
            description: "Total number of event deserialization errors");

        _topic = settings.Value.Topic;

        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = $"event-reader-{Guid.NewGuid()}", // Unique consumer group
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SecurityProtocol = settings.Value.SecurityProtocol,
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka consumer error: {Error}", error.Reason);
            })
            .Build();
    }

    public async Task<IEnumerable<Event>> GetEventsAfter(long position)
    {
        await _consumeLock.WaitAsync();
        
        using var activity = _activitySource.StartActivity(
            "Get Events After Position",
            ActivityKind.Consumer);
        
        activity?.SetTag("position", position);
        var sw = Stopwatch.StartNew();
        var events = new List<Event>();
    
        try
        {
            _logger.LogInformation(
                "Starting to read events after position {Position} from topic {Topic}",
                position, _topic);

            _consumer.Subscribe(_topic);
            
            // Ensure we're assigned partitions
            _consumer.Consume(TimeSpan.FromSeconds(5));
            
            foreach (var partition in _consumer.Assignment)
            {
                _consumer.Seek(new TopicPartitionOffset(_topic, partition.Partition, position));
            }

            var attempts = 0;
            const int maxAttempts = 10;

            while (attempts < maxAttempts)
            {
                var result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (result == null) 
                {
                    attempts++;
                    continue;
                }
                attempts = 0; // Reset counter if we got a message

                var @event = DeserializeEvent(result.Message);
                if (@event != null)
                {
                    events.Add(@event);
                    _eventReadCounter.Add(1);

                    ExtractContext(result.Message.Headers, activity);
                    
                    activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
                    activity?.SetTag("messaging.kafka.offset", result.Offset.Value);
                    
                    _logger.LogDebug(
                        "Read event {EventType} from partition {Partition} at offset {Offset}",
                        @event.GetType().FullName, result.Partition, result.Offset);
                }
            }

            sw.Stop();
            _readLatencyHistogram.Record(sw.ElapsedMilliseconds);
            
            _logger.LogInformation(
                "Successfully read {Count} events after position {Position} from topic {Topic} in {ElapsedMs}ms",
                events.Count, position, _topic, sw.ElapsedMilliseconds);
                
            return events;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex,
                "Failed to read events after position {Position} from topic {Topic}",
                position, _topic);
            throw;
        }
        finally
        {
            _consumer.Unsubscribe();
            _consumeLock.Release();
        }
    }

    private Event? DeserializeEvent(Message<string, string> message)
    {
        try
        {
            if (string.IsNullOrEmpty(message.Key))
                return null;

            var type = Type.GetType(message.Key);
            if (type == null)
                return null;

            return JsonSerializer.Deserialize(
                message.Value, 
                type, 
                _jsonSerializerOptions) as Event;
        }
        catch (Exception ex)
        {
            _deserializationErrorCounter.Add(1);
            _logger.LogError(ex,
                "Failed to deserialize event from message: {MessageValue}",
                message.Value);
            return null;
        }
    }

    private static void ExtractContext(Headers headers, Activity? activity)
    {
        if (activity == null || headers == null) return;

        var parentContext = Propagators.DefaultTextMapPropagator.Extract(
            default,
            headers,
            (headers, key) =>
            {
                if (headers.TryGetLastBytes(key, out var value))
                {
                    return new[] { System.Text.Encoding.UTF8.GetString(value) };
                }
                return Enumerable.Empty<string>();
            });

        activity.SetParentId(parentContext.ActivityContext.TraceId.ToString());
    }

    public void Dispose()
    {
        _consumer?.Dispose();
        _meter?.Dispose();
        _consumeLock.Dispose();
    }
}