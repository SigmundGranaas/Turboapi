using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.data;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.value;
using Xunit;
using Coordinates = Turboapi_geo.domain.value.Coordinates;


namespace Turboapi_geo.test.domain
{
    public class CreateLocationHandlerTests
    {
        private readonly IEventWriter _eventStore;
        private readonly IEventReader _eventReader;

        private readonly InMemoryLocationRead _writeRepository;
        private readonly CreateLocationHandler _handler;

        public CreateLocationHandlerTests()
        {
            var bus = new GeoSpatial.Tests.Doubles.TestMessageBus();
            _eventStore = new TestEventWriter(bus);
            _eventReader = new TestEventReader(bus);
            _handler = new CreateLocationHandler(_eventStore, new DummyProjector());
        }

        [Fact]
        public async Task Handle_WithValidCommand_ShouldCreateLocationAndPublishEvents()
        {
            var owner = Uuid7.NewUuid7();
            // Arrange
            var coordinates = new Coordinates()
            {
                Latitude = 123,
                Longitude = 321
            };

            var display = new DisplayInformation()
            {
                Name = "Test",
            };
            var command = new CreateLocationCommand(owner, coordinates, display);

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