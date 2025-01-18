using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.handler;
using Xunit;


namespace Turboapi_geo.test.domain.domain
{
    public class CreateLocationHandlerTests
    {
        private readonly IEventWriter _eventStore;
        private readonly IEventReader _eventReader;

        private readonly InMemoryLocationReadModel _writeRepository;
        private readonly GeometryFactory _geometryFactory;
        private readonly CreateLocationHandler _handler;

        public CreateLocationHandlerTests()
        {
            var bus = new TestMessageBus();
            _eventStore = new TestEventWriter(bus);
            _eventReader = new TestEventReader(bus);
            _geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
            _handler = new CreateLocationHandler( _eventStore, _geometryFactory);
        }

        [Fact]
        public async Task Handle_WithValidCommand_ShouldCreateLocationAndPublishEvents()
        {
            var owner = Uuid7.NewUuid7();
            // Arrange
            var command = new Commands.CreateLocationCommand(owner, 13.404954, 52.520008);

            // Act
            var locationId = await _handler.Handle(command);

            // Assert
            var e = await _eventReader.GetEventsAfter(0);
            var createdEvent = Assert.Single(e);
            var locationCreated = Assert.IsType<LocationCreated>(createdEvent);
            Assert.Equal(locationId, locationCreated.LocationId);
        }
    }
}