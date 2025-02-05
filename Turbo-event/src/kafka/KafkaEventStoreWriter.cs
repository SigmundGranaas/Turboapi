using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Turbo_event.kafka;
using Turbo_event.util;

namespace Turbo_event.test.kafka;

public class KafkaEventStoreWriter : IEventStoreWriter, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly ITopicInitializer _topicInitializer;
    private readonly IEventTopicResolver _topicResolver;
    private readonly ILogger<KafkaEventStoreWriter> _logger;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    private readonly Counter<long> _eventWriteCounter;
    private readonly Histogram<double> _writeLatencyHistogram;
    private readonly Counter<long> _writeErrorCounter;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public KafkaEventStoreWriter(
        ITopicInitializer topicInitializer,
        IEventTopicResolver topicResolver,
        JsonSerializerOptions jsonSerializerOptions,
        IOptions<KafkaSettings> settings,
        ILogger<KafkaEventStoreWriter> logger)
    {
        _jsonSerializerOptions = jsonSerializerOptions;
        _topicResolver = topicResolver;
        _topicInitializer = topicInitializer;
        _logger = logger;
        
        _activitySource = new ActivitySource("KafkaEventStoreWriter");
        _meter = new Meter("KafkaEventStoreWriter");
        
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

        _topic = settings.Value.Topic;

        var config = new ProducerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            EnableIdempotence = settings.Value.EnableIdempotence,
            SecurityProtocol = settings.Value.SecurityProtocol,
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => 
            {
                _writeErrorCounter.Add(1);
                _logger.LogError("Kafka producer error: {Error}", error.Reason);
            })
            .Build();
    }

    public async Task AppendEvents(IEnumerable<Event> events)
    {
        var eventsList = events.ToList();

        foreach (var @event in eventsList)
        {
            var topic = _topicResolver.ResolveTopicFor(@event);

            await _topicInitializer.EnsureTopicExists(topic);
        }
        
        using var activity = _activitySource.StartActivity(
            "Append Events",
            ActivityKind.Producer);
            
        var sw = Stopwatch.StartNew();
        try 
        {
            _logger.LogInformation(
                "Starting to append {Count} events to topic {Topic}", 
                eventsList.Count, _topic);
            
            foreach (var @event in eventsList)
            {
                var topic = _topicResolver.ResolveTopicFor(@event);
                await _topicInitializer.EnsureTopicExists(topic);
                
                var message = new Message<string, string>
                {
                    Key = @event.GetType().Name,
                    Value = JsonSerializer.Serialize(@event, _jsonSerializerOptions),
                    Headers = CreateHeaders(activity)
                };

                var result = await _producer.ProduceAsync(topic, message);
                
                _eventWriteCounter.Add(1);
                activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", result.Offset.Value);
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

    private static Headers CreateHeaders(Activity? activity)
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
