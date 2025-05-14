using System.Diagnostics;
using GeoSpatial.Domain.Events;
using NetTopologySuite.Geometries;
using Turboapi_geo.data;
using Turboapi_geo.data.model;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.domain.value;
using Turboapi_geo.infrastructure;
using Coordinates = Turboapi_geo.domain.value.Coordinates;
using Location = Turboapi_geo.domain.model.Location;


namespace GeoSpatial.Tests.Doubles
{
    public class InMemoryLocationWriteRepository : ILocationWriteRepository
    {
        private readonly Dictionary<Guid, LocationEntity> _locations;
        private readonly ILogger<InMemoryLocationWriteRepository>? _logger;

        public InMemoryLocationWriteRepository(
            Dictionary<Guid, LocationEntity> locations, 
            ILogger<InMemoryLocationWriteRepository>? logger = null)
        {
            _locations = locations;
            _logger = logger;
        }

        public Task<LocationEntity?> GetById(Guid id)
        {
            _locations.TryGetValue(id, out var location);
            return Task.FromResult(location);
        }

        public Task Add(LocationEntity location)
        {
            _locations[location.Id] = location;
            return Task.CompletedTask;
        }

        public Task Update(LocationEntity location)
        {
            _locations[location.Id] = location;
            return Task.CompletedTask;
        }

        public Task Delete(LocationEntity location)
        {
            _locations.Remove(location.Id);
            return Task.CompletedTask;
        }
        
        public Task UpdatePartial(Guid id, Coordinates? coordinates, DisplayUpdate? display)
        {
            if (_locations.TryGetValue(id, out var location))
            {
                if (coordinates != null)
                {
                    location.Geometry = coordinates.ToPoint(new GeometryFactory());
                }
                if (display != null)
                {
                    if (display.Name != null)
                    {
                        location.Name = display.Name;
                    }
                    if (display.Description != null)
                    {
                        location.Description = display.Description;
                    }
                    if (display.Icon != null)
                    {
                        location.Icon = display.Icon;
                    }
                }
            }
            else
            {
                _logger?.LogWarning("Failed to update position for location {LocationId} - entity not found", id);
            }
            
            return Task.CompletedTask;
        }
    }
    
    public class InMemoryLocationRead : ILocationReadRepository
    {
        private readonly Dictionary<Guid, LocationEntity> _locations;

        public InMemoryLocationRead(Dictionary<Guid, LocationEntity> locations)
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

            var displayInformation = new DisplayInformation(location.Name, location.Description, location.Icon);
           
            return Task.FromResult<Location?>(Location.Reconstitute(location.Id, location.OwnerId, Coordinates.FromPoint(location.Geometry), displayInformation));
        }

        public Task<IEnumerable<Location>> GetLocationsInExtent(
            Guid ownerId,
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
        
            return Task.FromResult(locations.Select(loc => Location.Reconstitute(loc.Id, loc.OwnerId, Coordinates.FromPoint(loc.Geometry), new DisplayInformation(loc.Name, loc.Description, loc.Icon))));
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
            LocationUpdated e => e.LocationId,
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

    public class DummyProjector : IDirectReadModelProjector
    {
        public Task ProjectEventsAsync(IReadOnlyList<DomainEvent> events,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
