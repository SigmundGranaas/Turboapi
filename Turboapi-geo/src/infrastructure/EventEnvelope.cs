using Turboapi_geo.domain.events;

namespace Turboapi_geo.infrastructure;

public class EventEnvelope
{
    public DomainEvent Event { get; }
    public Guid AggregateId { get; }
    public long Version { get; }
    public long Position { get; }

    public EventEnvelope(DomainEvent @event, Guid aggregateId, long version, long position)
    {
        Event = @event;
        AggregateId = aggregateId;
        Version = version;
        Position = position;
    }
}