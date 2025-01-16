using System.Diagnostics;
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

public class KafkaEventStore : IEventWriter, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly ILogger<KafkaEventStore> _logger;
    private readonly IEventPublisher _publisher;
    private readonly ActivitySource _activitySource;

    public KafkaEventStore(
        IOptions<KafkaSettings> settings,
        IEventPublisher publisher,
        ILogger<KafkaEventStore> logger)
    {
        _publisher = publisher;
        _logger = logger;
        _activitySource = new ActivitySource("KafkaEventStore");

        var config = new ProducerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
            MessageTimeoutMs = 5000,
            EnableIdempotence = true // Enable exactly-once semantics
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
        _topic = settings.Value.LocationEventsTopic;
    }

    public async Task AppendEvents(IEnumerable<DomainEvent> events)
    {
        using var activity = _activitySource.StartActivity(
            "Append Events",
            ActivityKind.Producer);

        try
        {
            foreach (var @event in events)
            {
                var headers = new Headers();
                
                // Inject trace context
                if (activity != null)
                {
                    var propagationContext = new PropagationContext(activity.Context, Baggage.Current);
                    Propagators.DefaultTextMapPropagator.Inject(propagationContext, headers,
                        (headers, key, value) => headers.Add(key, Encoding.UTF8.GetBytes(value)));

                    activity.SetTag("messaging.system", "kafka");
                    activity.SetTag("messaging.destination", _topic);
                    activity.SetTag("messaging.event_type", @event.GetType().Name);
                }
        

                var message = new Message<string, string>
                {
                    Key = @event.GetType().Name,
                    Value = JsonSerializer.Serialize(@event, JsonConfig.CreateDefault()),
                    Headers = headers
                };

                // Persist to Kafka
                var result = await _producer.ProduceAsync(_topic, message);
                
                activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", result.Offset.Value);

                // Publish to subscribers
                await _publisher.Publish(@event);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to append events to Kafka event store");
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}