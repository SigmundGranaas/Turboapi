using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query.model;
using Xunit;

namespace Turboapi_geo.test.domain.domain;

 public class DeleteLocationHandlerTests
    {
        private readonly IEventWriter _eventStore;
        private readonly IEventReader _reader;

        private readonly InMemoryLocationWriteRepository _writeRepository;
        private readonly InMemoryLocationReadModel _readRepository;

        private readonly DeleteLocationHandler _handler;
        private readonly GeometryFactory _geometryFactory;

        public DeleteLocationHandlerTests()
        {
            var dict = new Dictionary<Guid, LocationReadEntity>();
            var bus = new TestMessageBus();
            _reader = new TestEventReader(bus);
            _eventStore = new TestEventWriter(bus);
            
            _readRepository = new InMemoryLocationReadModel(dict);
            _writeRepository = new InMemoryLocationWriteRepository(dict);
            _handler = new DeleteLocationHandler( _eventStore, _readRepository);
            _geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
        }

        [Fact]
        public async Task Handle_WithValidCommand_ShouldDeleteLocationAndPublishEvents()
        {
            var owner = Uuid7.NewUuid7();
            // Arrange
            var location = Location.Create(
                owner.ToString(),
                _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
            );
            var dto = new LocationReadEntity();
            dto.Geometry = location.Geometry;
            dto.Id = location.Id;
            dto.OwnerId = location.OwnerId;
            _writeRepository.Add(dto);
            
            var command = new Commands.DeleteLocationCommand(location.Id, owner);

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
            var command = new Commands.DeleteLocationCommand(Guid.NewGuid(), Guid.NewGuid());

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
                owner.ToString(),
                _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
            );
            var dto = new LocationReadEntity();
            dto.Geometry = location.Geometry;
            dto.Id = location.Id;
            dto.OwnerId = location.OwnerId;
            _writeRepository.Add(dto);
            
            var command = new Commands.DeleteLocationCommand(location.Id, invalidOwner);
            
            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedException>(
                () => _handler.Handle(command)
            );
            
            // Verify no events were published
            var events = await _reader.GetEventsAfter(0);
            Assert.Empty(events);

            // Verify location was not modified
            var unchangedLocation = await _readRepository.GetById(location.Id);
            Assert.Equal(13.404954, unchangedLocation.Geometry.X, 2);
            Assert.Equal(52.520008, unchangedLocation.Geometry.Y, 2);
        }
    }