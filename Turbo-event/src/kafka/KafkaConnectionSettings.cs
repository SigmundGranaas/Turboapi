using Confluent.Kafka;

namespace Turbo_event.kafka;

public class KafkaSettings
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string ConsumerGroupId { get; set; } = string.Empty;
    public bool EnableIdempotence { get; set; } = true;
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;
}
