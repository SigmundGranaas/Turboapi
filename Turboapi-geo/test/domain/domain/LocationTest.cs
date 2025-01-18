using NetTopologySuite.Geometries;
using Turboapi_geo.domain.events;
using Xunit;

namespace Turboapi_geo.test.domain.domain;

public class LocationTests
{
    private readonly GeometryFactory _geometryFactory;

    public LocationTests()
    {
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
    }

    [Fact]
    public void Create_WithValidData_ShouldCreateLocationWithEvents()
    {
        // Arrange
        var ownerId = "owner123";
        var point = _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008));

        // Act
        var location = Location.Create(ownerId, point);

        // Assert
        Assert.NotEqual(Guid.Empty, location.Id);
        Assert.Equal(ownerId, location.OwnerId);
        Assert.Equal(point, location.Geometry);
            
        var createdEvent = Assert.Single(location.Events);
        var locationCreated = Assert.IsType<LocationCreated>(createdEvent);
        Assert.Equal(location.Id, locationCreated.LocationId);
        Assert.Equal(point, locationCreated.Geometry);
    }

    [Fact]
    public void Update_WithNewGeometry_ShouldUpdateAndCreateEvent()
    {
        // Arrange
        var location = Location.Create(
            "owner123", 
            _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );
        var newPoint = _geometryFactory.CreatePoint(new Coordinate(13.405, 52.520));

        // Act
        location.UpdatePosition(newPoint);

        // Assert
        Assert.Equal(newPoint, location.Geometry);
        Assert.Equal(2, location.Events.Count);
        Assert.IsType<LocationPositionChanged>(location.Events.Last());
    }
}