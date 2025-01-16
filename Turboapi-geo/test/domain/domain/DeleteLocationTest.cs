using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
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
            // Arrange
            var location = Location.Create(
                "owner123",
                _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
            );
            var dto = new LocationReadEntity();
            dto.Geometry = location.Geometry;
            dto.Id = location.Id;
            dto.OwnerId = location.OwnerId;
            _writeRepository.Add(dto);
            
            var command = new Commands.DeleteLocationCommand(location.Id);

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
            var command = new Commands.DeleteLocationCommand(Guid.NewGuid());

            // Act & Assert
            await Assert.ThrowsAsync<LocationNotFoundException>(
                () => _handler.Handle(command)
            );
        }
    }