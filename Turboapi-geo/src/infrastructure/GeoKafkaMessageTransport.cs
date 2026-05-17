using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turbo.Messaging;
using Turboapi.infrastructure;

namespace Turboapi_geo.infrastructure;

/// <summary>
/// Transitional bridge: publishes an <see cref="EventEnvelope"/> to the Kafka
/// topic the existing wire format expects. Step 5 of the messaging refactor
/// swaps this out for the NATS JetStream implementation; nothing in core
/// depends on this class.
/// </summary>
public sealed class GeoKafkaMessageTransport : IMessageTransport, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly Func<EventEnvelope, string> _topicFor;
    private readonly ILogger<GeoKafkaMessageTransport> _logger;

    public GeoKafkaMessageTransport(
        IOptions<KafkaSettings> settings,
        Func<EventEnvelope, string> topicFor,
        ILogger<GeoKafkaMessageTransport> logger)
    {
        _topicFor = topicFor;
        _logger = logger;
        var config = new ProducerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var topic = _topicFor(envelope);
        var headers = new Headers();
        foreach (var kvp in envelope.Headers)
            headers.Add(kvp.Key, Encoding.UTF8.GetBytes(kvp.Value));

        var shortName = envelope.Type[(envelope.Type.LastIndexOf('.') + 1)..];
        var message = new Message<string, string>
        {
            Key = shortName,
            Value = Encoding.UTF8.GetString(envelope.Data.Span),
            Headers = headers,
        };

        await _producer.ProduceAsync(topic, message, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
