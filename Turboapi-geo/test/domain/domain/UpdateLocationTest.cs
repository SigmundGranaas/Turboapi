using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Xunit;

namespace Turboapi_geo.test.domain.domain;

  public class UpdateLocationPositionHandlerTests
{
    private readonly ITestMessageBus _messageBus;
    private readonly IEventWriter _eventWriter;
    private readonly IEventReader _eventReader;
    private readonly ILocationReadModelRepository _readRepository;
    private readonly Dictionary<Guid, LocationReadEntity> _store;
    private readonly GeometryFactory _geometryFactory;
    private readonly UpdateLocationPositionHandler _handler;

    public UpdateLocationPositionHandlerTests()
    {
        _store = new Dictionary<Guid, LocationReadEntity>();
        _messageBus = new TestMessageBus();
        _eventWriter = new TestEventWriter(_messageBus);
        _eventReader = new TestEventReader(_messageBus);
        _readRepository = new InMemoryLocationReadModel(_store);
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        _handler = new UpdateLocationPositionHandler(
            _readRepository, 
            _eventWriter, 
            _geometryFactory);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldUpdateLocationAndPublishEvents()
    {
        var owner = Uuid7.NewUuid7();
        // Arrange
        var location = Location.Create(
            owner.ToString(),
            _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );

        var locationEntity = new LocationReadEntity
        {
            Geometry = location.Geometry,
            Id = location.Id,
            OwnerId = location.OwnerId
        };
        _store.Add(locationEntity.Id, locationEntity);

        var command = new Commands.UpdateLocationPositionCommand(
            owner,
            location.Id, 
            13.405, 
            52.520);

        // Act
        await _handler.Handle(command);

        // Assert
        var updatedLocation = await _readRepository.GetById(location.Id);
        Assert.NotNull(updatedLocation);
        Assert.Equal(command.Longitude, updatedLocation.Geometry.X, 2);
        Assert.Equal(command.Latitude, updatedLocation.Geometry.Y, 2);

        // Verify the event was published
        var events = await _eventReader.GetEventsForAggregate(location.Id);
        var positionChangedEvent = Assert.IsType<LocationPositionChanged>(
            events.Single()
        );
        Assert.Equal(location.Id, positionChangedEvent.LocationId);
        Assert.Equal(command.Longitude, positionChangedEvent.Geometry.X, 2);
        Assert.Equal(command.Latitude, positionChangedEvent.Geometry.Y, 2);
    }

    [Fact]
    public async Task Handle_WithNonExistentLocation_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new Commands.UpdateLocationPositionCommand(
            Guid.NewGuid(), 
            Guid.NewGuid(), 
            13.405, 
            52.520);

        // Act & Assert
        await Assert.ThrowsAsync<LocationNotFoundException>(
            () => _handler.Handle(command)
        );

        // Verify no events were published
        var events = await _eventReader.GetEventsAfter(0);
        Assert.Empty(events);
    }
    
    [Fact]
    public async Task Handle_WithIncorrectOwner_ShouldThrowNotFoundException()
    {
        // Arrange
        var correctOwner = Uuid7.NewUuid7();
        var incorrectOwner = Uuid7.NewUuid7();
    
        var location = Location.Create(
            correctOwner.ToString(),
            _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );

        var locationEntity = new LocationReadEntity
        {
            Geometry = location.Geometry,
            Id = location.Id,
            OwnerId = location.OwnerId
        };
        _store.Add(locationEntity.Id, locationEntity);

        var command = new Commands.UpdateLocationPositionCommand(
            incorrectOwner,  // Using incorrect owner ID
            location.Id,     // Existing location ID
            13.405, 
            52.520);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.Handle(command)
        );

        // Verify no events were published
        var events = await _eventReader.GetEventsAfter(0);
        Assert.Empty(events);

        // Verify location was not modified
        var unchangedLocation = await _readRepository.GetById(location.Id);
        Assert.Equal(13.404954, unchangedLocation.Geometry.X, 2);
        Assert.Equal(52.520008, unchangedLocation.Geometry.Y, 2);
    }
}