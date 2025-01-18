using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using GeoSpatial.Domain.Events;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Turboapi_geo.domain.events;
using Turboapi_geo.geo;
using Turboapi.infrastructure;

namespace Turboapi_geo.infrastructure;

public class KafkaEventWriter : IEventWriter, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private IKafkaTopicInitializer _topicInitializer;

    private readonly ILogger<KafkaEventWriter> _logger;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private bool _topicCreated;

    private readonly Counter<long> _eventWriteCounter;
    private readonly Histogram<double> _writeLatencyHistogram;
    private readonly Counter<long> _writeErrorCounter;

    public KafkaEventWriter(
        IKafkaTopicInitializer topicInitializer,
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventWriter> logger)
    {
        _topicInitializer = topicInitializer;

        _logger = logger;
        _activitySource = new ActivitySource("KafkaEventWriter");
        _meter = new Meter("KafkaEventWriter");
        
        // Initialize metrics
        _eventWriteCounter = _meter.CreateCounter<long>(
            "event_store_writes_total",
            description: "Total number of events written to the store");
            
        _writeLatencyHistogram = _meter.CreateHistogram<double>(
            "event_store_write_duration_ms",
            unit: "ms",
            description: "Time taken to write events to the store");
            
        _writeErrorCounter = _meter.CreateCounter<long>(
            "event_store_write_errors_total",
            description: "Total number of write errors");

        _topic = settings.Value.LocationEventsTopic;

        var config = new ProducerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            EnableIdempotence = true,
            SecurityProtocol = SecurityProtocol.Plaintext,
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => 
            {
                _writeErrorCounter.Add(1);
                _logger.LogError("Kafka producer error: {Error}", error.Reason);
            })
            .Build();
            
        _logger.LogInformation(
            "Initialized Kafka event writer for topic {Topic} with bootstrap servers {Servers}", 
            _topic, settings.Value.BootstrapServers);
    }
    
    
    public async Task AppendEvents(IEnumerable<DomainEvent> events)
    {
        await _topicInitializer.EnsureTopicExists(_topic);

        using var activity = _activitySource.StartActivity(
            "Append Events",
            ActivityKind.Producer);
            
        var sw = Stopwatch.StartNew();
        var eventsList = events.ToList();
        
        try 
        {

            _logger.LogInformation(
                "Starting to append {Count} events to topic {Topic}", 
                eventsList.Count, _topic);
            
            foreach (var @event in eventsList)
            {
                var headers = new Headers();

                var message = new Message<string, string>
                {
                    Key = @event.GetType().Name,
                    Value = JsonSerializer.Serialize(@event, JsonConfig.CreateDefault()),
                    Headers = headers
                };
                var result = await _producer.ProduceAsync(_topic, message);
                
                _eventWriteCounter.Add(1);
                activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", result.Offset.Value);
                
                _logger.LogDebug(
                    "Appended event {EventType} with ID {EventId} to partition {Partition} at offset {Offset}",
                    @event.GetType().Name, @event.Id, result.Partition.Value, result.Offset.Value);
            }

            sw.Stop();
            _writeLatencyHistogram.Record(sw.ElapsedMilliseconds);
            
            _logger.LogInformation(
                "Successfully appended {Count} events to topic {Topic} in {ElapsedMs}ms",
                eventsList.Count, _topic, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _writeErrorCounter.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, 
                "Failed to append {Count} events to topic {Topic}", 
                eventsList.Count, _topic);
            throw;
        }
    }

    private Headers CreateHeaders(Activity? activity)
    {
        var headers = new Headers();
        
        if (activity != null)
        {
            var propagationContext = new PropagationContext(activity.Context, Baggage.Current);
            Propagators.DefaultTextMapPropagator.Inject(propagationContext, headers,
                (headers, key, value) => headers.Add(key, Encoding.UTF8.GetBytes(value)));
        }

        return headers;
    }

    public void Dispose()
    {
        _producer?.Dispose();
        _meter?.Dispose();
    }
}

public class KafkaEventReader : IEventReader, IDisposable
{
    private readonly IConsumer<string, string> _consumer;
    private readonly string _topic;
    private readonly ILogger<KafkaEventReader> _logger;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly Counter<long> _eventReadCounter;
    private readonly Histogram<double> _readLatencyHistogram;
    private readonly Counter<long> _deserializationErrorCounter;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public KafkaEventReader(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventReader> logger)
    {
        _logger = logger;
        _activitySource = new ActivitySource("KafkaEventReader");
        _meter = new Meter("KafkaEventReader");
        
        // Initialize metrics
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

        _topic = settings.Value.LocationEventsTopic;

        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            GroupId = "event-reader",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka consumer error: {Error}", error.Reason);
            })
            .Build();
            
        _logger.LogInformation(
            "Initialized Kafka event reader for topic {Topic} with bootstrap servers {Servers}", 
            _topic, settings.Value.BootstrapServers);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<IEnumerable<DomainEvent>> GetEventsAfter(long position)
    {
        using var activity = _activitySource.StartActivity(
            "Get Events After Position",
            ActivityKind.Consumer);
        
        activity?.SetTag("position", position);
        var sw = Stopwatch.StartNew();
        var events = new List<DomainEvent>();
    
        try
        {
            _logger.LogInformation(
                "Starting to read events after position {Position} from topic {Topic}",
                position, _topic);

            _consumer.Subscribe(_topic);
            _consumer.Seek(new TopicPartitionOffset(_topic, 0, position));

            while (true)
            {
                var result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (result == null) break;

                var @event = DeserializeEvent(result.Message);
                if (@event != null)
                {
                    events.Add(@event);
                    _eventReadCounter.Add(1);
                
                    _logger.LogDebug(
                        "Read event {EventType} from partition {Partition} at offset {Offset}",
                        @event.GetType().Name, result.Partition, result.Offset);
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
        }
    }
    
    public async Task<IEnumerable<DomainEvent>> GetEventsForAggregate(Guid aggregateId)
    {
        using var activity = _activitySource.StartActivity(
            "Get Events For Aggregate",
            ActivityKind.Consumer);
            
        activity?.SetTag("aggregate.id", aggregateId);
        var sw = Stopwatch.StartNew();
        var events = new List<DomainEvent>();
        
        try
        {
            _logger.LogInformation(
                "Starting to read events for aggregate {AggregateId} from topic {Topic}",
                aggregateId, _topic);

            _consumer.Subscribe(_topic);
            _consumer.Seek(new TopicPartitionOffset(_topic, 0, Offset.Beginning));

            while (true)
            {
                var result = _consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (result == null) break;

                var @event = DeserializeEvent(result.Message);
                if (@event?.Id == aggregateId)
                {
                    events.Add(@event);
                    _eventReadCounter.Add(1);
                    
                    _logger.LogDebug(
                        "Read event {EventType} for aggregate {AggregateId} from partition {Partition} at offset {Offset}",
                        @event.GetType().Name, aggregateId, result.Partition, result.Offset);
                }
            }

            sw.Stop();
            _readLatencyHistogram.Record(sw.ElapsedMilliseconds);
            
            _logger.LogInformation(
                "Successfully read {Count} events for aggregate {AggregateId} from topic {Topic} in {ElapsedMs}ms",
                events.Count, aggregateId, _topic, sw.ElapsedMilliseconds);
                
            return events;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex,
                "Failed to read events for aggregate {AggregateId} from topic {Topic}",
                aggregateId, _topic);
            throw;
        }
        finally
        {
            _consumer.Unsubscribe();
        }
    }

    private DomainEvent? DeserializeEvent(Message<string, string> message)
    {
        try
        {
            return JsonSerializer.Deserialize<DomainEvent>(
                message.Value, 
                _jsonOptions);
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

    public void Dispose()
    {
        _consumer?.Dispose();
        _meter?.Dispose();
    }
}