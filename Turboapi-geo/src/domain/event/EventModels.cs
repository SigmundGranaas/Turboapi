using Turboapi_geo.domain.events;

namespace GeoSpatial.Domain.Events
{
    /// <summary>
    /// Subscribes to events
    /// </summary>
    public interface IEventSubscriber
    {
        /// <summary>
        /// Subscribes to a specific event type
        /// </summary>
        /// <typeparam name="T">Type of event to subscribe to</typeparam>
        /// <param name="handler">Handler for the event</param>
        void Subscribe<T>(Func<T, Task> handler) where T : DomainEvent;

        /// <summary>
        /// Unsubscribes from a specific event type
        /// </summary>
        /// <typeparam name="T">Type of event to unsubscribe from</typeparam>
        /// <param name="handler">Handler to remove</param>
        void Unsubscribe<T>(Func<T, Task> handler) where T : DomainEvent;
    }
    
    public interface IEventWriter
    {
        /// <summary>
        /// Appends events to the event stream
        /// </summary>
        Task AppendEvents(IEnumerable<DomainEvent> events);
    }

    public interface IEventReader
    {
        /// <summary>
        /// Retrieves all events for a specific aggregate
        /// </summary>
        Task<IEnumerable<DomainEvent>> GetEventsForAggregate(Guid aggregateId);
    
        /// <summary>
        /// Retrieves events after a specific position
        /// </summary>
        Task<IEnumerable<DomainEvent>> GetEventsAfter(long position);
    }
}