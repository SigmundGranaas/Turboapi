
public interface IEventStoreWriter
{
    Task AppendEvents(IEnumerable<Event> events);
}
