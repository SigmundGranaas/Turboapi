using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
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
    private readonly JsonSerializerOptions _jsonSerializerOptions; // Store options

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
        _jsonSerializerOptions = JsonConfig.CreateDefault(); // Initialize options once

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
                var headers = CreateHeaders(activity); // Ensure CreateHeaders is called correctly

                string jsonValue = JsonSerializer.Serialize(@event, @event.GetType(), _jsonSerializerOptions);

                _logger.LogDebug("Serializing event {EventType} (ID: {EventId}) to JSON: {JsonPayload}", @event.GetType().Name, @event.Id, jsonValue);


                var message = new Message<string, string>
                {
                    Key = @event.GetType().Name, // Using the type name as key is good for routing/filtering
                    Value = jsonValue,
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
                (carrier, key, value) => carrier.Add(key, Encoding.UTF8.GetBytes(value)));
        }

        return headers;
    }

    public void Dispose()
    {
        _producer?.Dispose();
        _meter?.Dispose();
    }
}