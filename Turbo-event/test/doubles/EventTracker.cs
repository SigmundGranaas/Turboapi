
using Microsoft.Extensions.Logging;

namespace Turboapi.Tests
{
    /// <summary>
    /// Thread-safe event tracker for testing
    /// </summary>
    public class EventTracker<TEvent> where TEvent : Event
    {
        private readonly List<TEvent> _events;
        private readonly object _lock = new();
        
        // Standard constructor for new tracker
        public EventTracker()
        {
            _events = new List<TEvent>();
        }
        
        // Constructor that accepts existing list (for tests that need to track across instances)
        public EventTracker(List<TEvent> existingList)
        {
            _events = existingList;
        }

        public void Add(TEvent @event)
        {
            lock (_lock)
            {
                _events.Add(@event);
            }
        }
        
        public void Clear()
        {
            lock (_lock)
            {
                _events.Clear();
            }
        }
        
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _events.Count;
                }
            }
        }
        
        public List<TEvent> GetEvents()
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }
        
        public bool Any(Func<TEvent, bool> predicate)
        {
            lock (_lock)
            {
                return _events.Any(predicate);
            }
        }
    }
    
    /// <summary>
    /// Generic event handler that adds events to the tracker
    /// </summary>
    public class TrackedEventHandler<TEvent> : IEventHandler<TEvent> where TEvent : Event
    {
        private readonly EventTracker<TEvent> _eventTracker;
        private readonly ILogger? _logger;
        
        public TrackedEventHandler(EventTracker<TEvent> eventTracker, ILogger? logger = null)
        {
            _eventTracker = eventTracker;
            _logger = logger;
        }
        
        public Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
        {
            _logger?.LogInformation("Handling event: {EventType} - {EventId}", typeof(TEvent).Name, @event.Id);
            _eventTracker.Add(@event);
            return Task.CompletedTask;
        }
    }
}