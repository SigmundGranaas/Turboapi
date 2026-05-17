using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turbo.Messaging;

namespace Turboapi.Infrastructure.Messaging;

/// <summary>
/// Transitional bridge: forwards an <see cref="EventEnvelope"/> taken off the
/// outbox to the existing authentication-events Kafka topic so any external
/// consumer that already subscribes keeps working. Step 5 of the messaging
/// refactor replaces this with the NATS JetStream transport; nothing in the
/// Core or Application layers depends on this class.
/// </summary>
public sealed class AuthKafkaMessageTransport : IMessageTransport, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly ILogger<AuthKafkaMessageTransport> _logger;

    public AuthKafkaMessageTransport(
        IOptions<KafkaSettings> settings,
        ILogger<AuthKafkaMessageTransport> logger)
    {
        _topic = settings.Value.UserAccountsTopic
                 ?? throw new InvalidOperationException("Kafka:UserAccountsTopic is not configured");
        _logger = logger;
        var config = new ProducerConfig
        {
            BootstrapServers = settings.Value.BootstrapServers,
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var headers = new Headers();
        foreach (var kvp in envelope.Headers)
            headers.Add(kvp.Key, Encoding.UTF8.GetBytes(kvp.Value));

        envelope.Headers.TryGetValue("aggregateId", out var key);
        key ??= envelope.EventId.ToString();

        var message = new Message<string, string>
        {
            Key = key,
            Value = Encoding.UTF8.GetString(envelope.Data.Span),
            Headers = headers,
        };

        await _producer.ProduceAsync(_topic, message, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
