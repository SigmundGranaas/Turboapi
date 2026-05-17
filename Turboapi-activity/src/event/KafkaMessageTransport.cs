using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turbo.Messaging;
using Turbo_event.kafka;

namespace Turboauth_activity.infrastructure;

/// <summary>
/// Transitional bridge: publishes an <see cref="EventEnvelope"/> to the Kafka
/// topic an existing per-event-type consumer is already subscribed to, using
/// the same key/value shape <c>KafkaEventStoreWriter</c> produced before the
/// outbox was introduced. Step 5 of the messaging refactor swaps this out for
/// the NATS JetStream implementation; nothing in core depends on this class.
/// </summary>
public sealed class KafkaMessageTransport : IMessageTransport, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly Func<EventEnvelope, string> _topicFor;
    private readonly ILogger<KafkaMessageTransport> _logger;

    public KafkaMessageTransport(
        IOptions<KafkaSettings> settings,
        Func<EventEnvelope, string> topicFor,
        ILogger<KafkaMessageTransport> logger)
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

        var key = ShortName(envelope.Type);
        var message = new Message<string, string>
        {
            Key = key,
            Value = Encoding.UTF8.GetString(envelope.Data.Span),
            Headers = headers,
        };

        await _producer.ProduceAsync(topic, message, cancellationToken);
    }

    private static string ShortName(string type)
    {
        var lastDot = type.LastIndexOf('.');
        return lastDot < 0 ? type : type[(lastDot + 1)..];
    }

    public void Dispose() => _producer.Dispose();
}
