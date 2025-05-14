using NetTopologySuite.Geometries;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.value;
using Xunit;
using Coordinates = Turboapi_geo.domain.value.Coordinates;
using Location = Turboapi_geo.domain.model.Location;

namespace Turboapi_geo.test.domain;

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
        var ownerId = Guid.NewGuid();
        var point = _geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008));

        var name = "Location";
        var desciption = "Description";
        var icon = "icon";

        var displayInformation = new DisplayInformation()
        {
            Name = name,
            Description = desciption,
            Icon = icon,
        };
        
        // Act
        var location = Location.Create(ownerId, Coordinates.FromPoint(point), displayInformation);

        // Assert
        Assert.NotEqual(Guid.Empty, location.Id);
        Assert.Equal(ownerId, location.OwnerId);
        Assert.Equal(point, location.Coordinates.ToPoint(_geometryFactory));
        Assert.Equal(name, location.Display.Name);
        Assert.Equal(desciption, location.Display.Description);
        Assert.Equal(icon, location.Display.Icon);

        var createdEvent = Assert.Single(location.Events);
        var locationCreated = Assert.IsType<LocationCreated>(createdEvent);
        Assert.Equal(location.Id, locationCreated.LocationId);
        Assert.Equal(point, locationCreated.Coordinates.ToPoint(_geometryFactory));
        Assert.Equal(name, locationCreated.Display.Name);
    }

    [Fact]
    public void Update_WithNewGeometry_ShouldUpdateAndCreateEvent()
    {
        // Arrange
        var location = Location.Create(
            Guid.NewGuid(), 
            Coordinates.FromPoint(_geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))),
                new DisplayInformation("name")
        );
        var newPoint = _geometryFactory.CreatePoint(new Coordinate(13.405, 52.520));

        var parameters = new LocationUpdateParameters(Coordinates.FromPoint(newPoint));
        // Act
        location.Update(location.OwnerId, parameters);

        // Assert
        Assert.Equal(newPoint, location.Coordinates.ToPoint(_geometryFactory));
        Assert.Equal(2, location.Events.Count);
        Assert.IsType<LocationUpdated>(location.Events.Last());
    }
}