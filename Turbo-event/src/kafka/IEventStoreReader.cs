namespace Turbo_event.kafka;

public interface IEventStoreReader
{
    Task<IEnumerable<Event>> GetEventsAfter(long position);
}