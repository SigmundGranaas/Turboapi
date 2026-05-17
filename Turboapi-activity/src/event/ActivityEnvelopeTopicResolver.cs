using Turbo.Messaging;

namespace Turboauth_activity.infrastructure;

/// <summary>
/// Maps an <see cref="EventEnvelope"/> to the Kafka topic the existing
/// per-event-type consumer subscribes to. Transitional; deleted in
/// step 5 when NATS replaces Kafka and subject names follow a fixed
/// convention.
/// </summary>
internal sealed class ActivityEnvelopeTopicResolver
{
    public string ResolveTopicFor(EventEnvelope envelope)
    {
        var shortName = envelope.Type[(envelope.Type.LastIndexOf('.') + 1)..];
        return shortName switch
        {
            "ActivityPositionCreated" => "location.create_command",
            _ => "activities",
        };
    }
}
