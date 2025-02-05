
public class TestMessageBus
{
    private readonly List<Event> _events = new();
    public IReadOnlyList<Event> Events => _events.AsReadOnly();
    public event EventHandler<Event>? OnEventPublished;

    public void Publish(Event @event)
    {
        _events.Add(@event);
        OnEventPublished?.Invoke(this, @event);
    }

    public void Clear() => _events.Clear();

    public IEnumerable<TEvent> GetEventsOfType<TEvent>() where TEvent : Event
        => _events.OfType<TEvent>();

    public bool HasEventOfType<TEvent>() where TEvent : Event
        => _events.OfType<TEvent>().Any();

    public int CountEventsOfType<TEvent>() where TEvent : Event
        => _events.OfType<TEvent>().Count();
}