
public class TestEventStoreWriter : IEventStoreWriter
{
    private readonly TestMessageBus _messageBus;
    private long _position;
    private readonly Dictionary<Guid, long> _versions = new();
    private readonly Func<Event, Guid> _aggregateIdResolver;

    public TestEventStoreWriter(
        TestMessageBus messageBus, 
        Func<Event, Guid> aggregateIdResolver)
    {
        _messageBus = messageBus;
        _aggregateIdResolver = aggregateIdResolver;
    }
    
    public TestEventStoreWriter(
        TestMessageBus messageBus)
    {
        _messageBus = messageBus;
        _aggregateIdResolver = (eventId) => eventId.Id;  ;
    }

    public Task AppendEvents(IEnumerable<Event> events)
    {
        foreach (var @event in events)
        {
            var aggregateId = _aggregateIdResolver(@event);
            if (!_versions.TryGetValue(aggregateId, out var version))
            {
                version = 0;
            }

            version++;
            _versions[aggregateId] = version;

            var position = Interlocked.Increment(ref _position);
            _messageBus.Publish(@event);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<Guid, long> Versions => _versions;
    public long Position => _position;
}