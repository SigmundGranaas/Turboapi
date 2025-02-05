using Turboauth_activity.domain.events;

namespace Turboauth_activity.domain;

public abstract class AggregateRoot
{
    private readonly List<Event> _events = new();
    public IReadOnlyList<Event> Events => _events.AsReadOnly();
    protected void AddEvent(Event @event) => _events.Add(@event);
    public void ClearEvents() => _events.Clear();
}
