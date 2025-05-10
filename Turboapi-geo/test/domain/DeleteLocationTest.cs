using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.data.model;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.domain.value;
using Xunit;
using Coordinates = Turboapi_geo.domain.value.Coordinates;
using Location = Turboapi_geo.domain.model.Location;

namespace Turboapi_geo.test.domain;

 public class DeleteLocationHandlerTests
    {
        private readonly IEventWriter _eventStore;
        private readonly IEventReader _reader;

        private readonly InMemoryLocationWriteRepository _writeRepository;
        private readonly InMemoryLocationRead _readRepository;

        private readonly DeleteLocationHandler _handler;
        private readonly GeometryFactory _geometryFactory;

        public DeleteLocationHandlerTests()
        {
            var dict = new Dictionary<Guid, LocationEntity>();
            var bus = new GeoSpatial.Tests.Doubles.TestMessageBus();
            _reader = new TestEventReader(bus);
            _eventStore = new TestEventWriter(bus);
            
            _readRepository = new InMemoryLocationRead(dict);
            _writeRepository = new InMemoryLocationWriteRepository(dict);
            _handler = new DeleteLocationHandler( _eventStore, _readRepository, new DummyProjector());
            _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        }

        [Fact]
        public async Task Handle_WithValidCommand_ShouldDeleteLocationAndPublishEvents()
        {
            var owner = Uuid7.NewUuid7();
            // Arrange
            var location = Location.Create(
                owner,
                Coordinates.FromPoint(_geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))),
                new DisplayInformation("name")
            );
            
            var dto = new LocationEntity()
            {
                Id = location.Id,
                Geometry = location.Coordinates.ToPoint(_geometryFactory),
                OwnerId = location.OwnerId,
                Name = location.Display.Name,
            };

            await _writeRepository.Add(dto);
            
            var command = new DeleteLocationCommand(owner, location.Id);

            // Act
            await _handler.Handle(command);
            
            var es = await _reader.GetEventsAfter(0);
            var deletedEvent = Assert.IsType<LocationDeleted>(
                es.Last()
            );
            Assert.Equal(location.Id, deletedEvent.LocationId);
            Assert.Equal(location.OwnerId, deletedEvent.OwnerId);
        }

        [Fact]
        public async Task Handle_WithNonExistentLocation_ShouldThrowNotFoundException()
        {
            // Arrange
            var command = new DeleteLocationCommand(Guid.NewGuid(), Guid.NewGuid());

            // Act & Assert
            await Assert.ThrowsAsync<LocationNotFoundException>(
                () => _handler.Handle(command)
            );
        }
        
        [Fact]
        public async Task Handle_WithIncorrectOwner_ShouldThrowNotFoundException()
        {
            var invalidOwner = Uuid7.NewUuid7();
            var owner = Uuid7.NewUuid7();
            // Arrange
            var location = Location.Create(
                owner,
                Coordinates.FromPoint(_geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))),
                new DisplayInformation("name")
            );
            var dto = new LocationEntity()
            {
                Id = location.Id,
                Geometry = location.Coordinates.ToPoint(_geometryFactory),
                OwnerId = location.OwnerId,
                Name = location.Display.Name,
            };
     
            _writeRepository.Add(dto);
            
            var command = new DeleteLocationCommand(location.Id, invalidOwner);
            
            // Act & Assert
            await Assert.ThrowsAsync<LocationNotFoundException>(
                () => _handler.Handle(command)
            );
            
            // Verify no events were published
            var events = await _reader.GetEventsAfter(0);
            Assert.Empty(events);

            // Verify location was not modified
            var unchangedLocation = await _readRepository.GetById(location.Id);
            Assert.Equal(13.404954, unchangedLocation.Coordinates.Longitude, 2);
            Assert.Equal(52.520008, unchangedLocation.Coordinates.Latitude, 2);
        }
    }