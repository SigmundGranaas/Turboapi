using Turboapi_geo.domain.events;

namespace Turboapi_geo.domain;

public abstract class AggregateRoot
{
    private readonly List<DomainEvent> _events = new();
    public IReadOnlyList<DomainEvent> Events => _events.AsReadOnly();
    protected void AddEvent(DomainEvent @event) => _events.Add(@event);
    public void ClearEvents() => _events.Clear();
}
