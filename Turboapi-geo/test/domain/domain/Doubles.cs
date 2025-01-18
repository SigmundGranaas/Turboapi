using GeoSpatial.Domain.Events;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;

namespace GeoSpatial.Tests.Doubles
{
    public class InMemoryLocationWriteRepository : ILocationWriteRepository
    {

        private readonly Dictionary<Guid, LocationReadEntity> _locations;

        public InMemoryLocationWriteRepository(Dictionary<Guid, LocationReadEntity> locations)
        {
            _locations = locations;
        }

        public Task Add(LocationReadEntity location)
        {
            _locations[location.Id] = location;
            return Task.CompletedTask;
        }

        public Task Update(LocationReadEntity location)
        {
            _locations[location.Id] = location;
            return Task.CompletedTask;
        }

        public Task Delete(LocationReadEntity location)
        {
            _locations.Remove(location.Id);
            return Task.CompletedTask;
        }

        public Task<LocationReadEntity?> GetById(Guid id)
        {
            _locations.TryGetValue(id, out var location);
            return Task.FromResult(location);
        }
    }
    
    public class InMemoryLocationReadModel : ILocationReadModelRepository
    {
        private readonly Dictionary<Guid, LocationReadEntity> _locations;

        public InMemoryLocationReadModel(Dictionary<Guid, LocationReadEntity> locations)
        {
            _locations = locations;
        }

        public Task<Location?> GetById(Guid id)
        {
            _locations.TryGetValue(id, out var location);
            if (location == null)
            {
                return Task.FromResult<Location?>(null);
            }
        
            return Task.FromResult<Location?>(Location.From(location.Id, location.OwnerId, location.Geometry));
        }

        public Task<IEnumerable<Location>> GetLocationsInExtent(
            string ownerId,
            double minLongitude,
            double minLatitude,
            double maxLongitude,
            double maxLatitude)
        {
            var locations = _locations.Values
                .Where(l => l.OwnerId == ownerId)
                .Where(l => l.Geometry.X >= minLongitude)
                .Where(l => l.Geometry.X <= maxLongitude)
                .Where(l => l.Geometry.Y >= minLatitude)
                .Where(l => l.Geometry.Y <= maxLatitude);
        
            return Task.FromResult(locations.Select(loc => Location.From(loc.Id, loc.OwnerId, loc.Geometry)));
        }
    }

    public interface ITestMessageBus
    {
        IReadOnlyList<EventEnvelope> Events { get; }
        void Publish(EventEnvelope envelope);
        void Clear();
        event EventHandler<DomainEvent> OnEventPublished;
    }

    public class TestMessageBus : ITestMessageBus
    {
        private readonly List<EventEnvelope> _events = new();
        public IReadOnlyList<EventEnvelope> Events => _events.AsReadOnly();
        public event EventHandler<DomainEvent> OnEventPublished;

        public void Publish(EventEnvelope envelope)
        {
            _events.Add(envelope);
            OnEventPublished?.Invoke(this, envelope.Event);
        }

        public void Clear() => _events.Clear();
    }

    public class TestEventWriter : IEventWriter
    {
        private readonly ITestMessageBus _messageBus;
        private long _position;
        private readonly Dictionary<Guid, long> _versions = new();

        public TestEventWriter(ITestMessageBus messageBus)
        {
            _messageBus = messageBus;
        }

        public Task AppendEvents(IEnumerable<DomainEvent> events)
        {
            foreach (var @event in events)
            {
                var aggregateId = GetAggregateId(@event);
                if (!_versions.TryGetValue(aggregateId, out var version))
                {
                    version = 0;
                }

                version++;
                _versions[aggregateId] = version;

                var position = Interlocked.Increment(ref _position);
                var envelope = new EventEnvelope(@event, aggregateId, version, position);
                _messageBus.Publish(envelope);
            }

            return Task.CompletedTask;
        }

        private static Guid GetAggregateId(DomainEvent @event) => @event switch
        {
            LocationCreated e => e.LocationId,
            LocationPositionChanged e => e.LocationId,
            LocationDeleted e => e.LocationId,
            _ => throw new ArgumentException($"Unknown event type: {@event.GetType()}")
        };
    }

    public class TestEventReader : IEventReader
    {
        private readonly ITestMessageBus _messageBus;

        public TestEventReader(ITestMessageBus messageBus)
        {
            _messageBus = messageBus;
        }

        public Task<IEnumerable<DomainEvent>> GetEventsForAggregate(Guid aggregateId)
        {
            var events = _messageBus.Events
                .Where(e => e.AggregateId == aggregateId)
                .OrderBy(e => e.Version)
                .Select(e => e.Event);
            return Task.FromResult(events);
        }

        public Task<IEnumerable<DomainEvent>> GetEventsAfter(long position)
        {
            var events = _messageBus.Events
                .Where(e => e.Position > position)
                .OrderBy(e => e.Position)
                .Select(e => e.Event);
            return Task.FromResult(events);
        }
    }

    public class TestEventSubscriber : IEventSubscriber
    {
        private readonly ITestMessageBus _messageBus;
        private readonly Dictionary<Type, List<Func<DomainEvent, Task>>> _handlers = new();

        public TestEventSubscriber(ITestMessageBus messageBus)
        {
            _messageBus = messageBus;
            _messageBus.OnEventPublished += HandleEvent;
        }

        public void Subscribe<T>(Func<T, Task> handler) where T : DomainEvent
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type))
            {
                _handlers[type] = new List<Func<DomainEvent, Task>>();
            }

            _handlers[type].Add(async evt => await handler((T)evt));
        }

        public void Unsubscribe<T>(Func<T, Task> handler) where T : DomainEvent
        {
            if (_handlers.TryGetValue(typeof(T), out var handlers))
            {
                handlers.RemoveAll(h => h.Target == handler.Target);
            }
        }

        private async void HandleEvent(object sender, DomainEvent @event)
        {
            var eventType = @event.GetType();
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    await handler(@event);
                }
            }
        }
    }
}
