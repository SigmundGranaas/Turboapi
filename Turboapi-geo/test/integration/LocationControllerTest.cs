using System.Security.Claims;
using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using Turboapi_geo.controller;
using Turboapi_geo.controller.request;
using Turboapi_geo.controller.response;
using Turboapi_geo.data.model;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Xunit;

public class LocationsControllerTests : IAsyncDisposable
{
    private readonly LocationsController _controller;
    private readonly InMemoryLocationWriteRepository _writeRepository;
    private readonly InMemoryLocationRead _read;
    private readonly ITestMessageBus _messageBus;
    private readonly IEventWriter _eventWriter;
    private readonly IEventReader _eventReader;
    private readonly TestEventSubscriber _eventSubscriber;
    private readonly LocationCreatedHandler _createdHandler;
    private readonly LocationDeletedHandler _deletedHandler;
    private readonly LocationUpdatedHandler _updateHandler;
    private readonly CancellationTokenSource _cts;

    public LocationsControllerTests()
    {
        // Initialize repositories
        var locationStore = new Dictionary<Guid, LocationEntity>();
        _writeRepository = new InMemoryLocationWriteRepository(locationStore);
        _read = new InMemoryLocationRead(locationStore);

        // Setup test event infrastructure
        _messageBus = new GeoSpatial.Tests.Doubles.TestMessageBus();
        _eventWriter = new TestEventWriter(_messageBus);
        _eventReader = new TestEventReader(_messageBus);
        _eventSubscriber = new TestEventSubscriber(_messageBus);
        
        // Initialize event handlers
        _createdHandler = new LocationCreatedHandler(
            _writeRepository, 
            new TestLogger<LocationCreatedHandler>());
        
        _deletedHandler = new LocationDeletedHandler(
            _writeRepository, 
            new TestLogger<LocationDeletedHandler>());

        _updateHandler = new LocationUpdatedHandler(
            _writeRepository, 
            new TestLogger<LocationUpdatedHandler>());
        
        // Subscribe handlers to events
        _eventSubscriber.Subscribe<LocationCreated>(evt => _createdHandler.HandleAsync(evt, _cts.Token));
        _eventSubscriber.Subscribe<LocationDeleted>(evt => _deletedHandler.HandleAsync(evt, _cts.Token));
        _eventSubscriber.Subscribe<LocationUpdated>(evt => _updateHandler.HandleAsync(evt, _cts.Token));
        
        // Initialize command handlers
        var createHandler = new CreateLocationHandler(_eventWriter, new DummyProjector());
        var updateHandler = new UpdateLocationHandler(_read, _eventWriter, new DummyProjector());
        var deleteHandler = new DeleteLocationHandler(_eventWriter, _read, new DummyProjector());

        var getByIdHandler = new GetLocationByIdHandler(_read);
        var getInExtentHandler = new GetLocationsInExtentHandler(_read);

        // Initialize controller
        _controller = new LocationsController(
            createHandler,
            updateHandler,
            deleteHandler,
            getByIdHandler,
            getInExtentHandler,
            new TestLogger<LocationsController>()
        );
        
        _cts = new CancellationTokenSource();
    }

    private void SetupControllerContext(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            }
        };
    }

    private GeometryData CreateGeometryData(double longitude, double latitude)
    {
        return new GeometryData
        {
            Longitude = longitude,
            Latitude = latitude
        };
    }

    private DisplayData CreateDisplayData(string name, string? description = null, string? icon = null)
    {
        return new DisplayData
        {
            Name = name,
            Description = description,
            Icon = icon
        };
    }
    
    private DisplayChangeset CreateDisplayUpdateData(string? name, string? description = null, string? icon = null)
    {
        return new DisplayChangeset()
        {
            Name = name,
            Description = description,
            Icon = icon
        };
    }

    private async Task<Guid> CreateTestLocation(
        Guid ownerId, 
        double longitude, 
        double latitude, 
        string name,
        string? description = null,
        string? icon = null)
    {
        SetupControllerContext(ownerId);

        var geometry = CreateGeometryData(longitude, latitude);
        var display = CreateDisplayData(name, description, icon);

        var request = new CreateLocationRequest
        {
            Geometry = geometry,
            Display = display
        };

        var result = await _controller.Create(request);
        var createdAtResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<LocationResponse>(createdAtResult.Value);
        return response.Id;
    }

    [Fact]
    public async Task Create_ShouldCreateLocationAndUpdateReadModel()
    {
        // Arrange
        var owner = Guid.NewGuid();
        SetupControllerContext(owner);

        var geometry = CreateGeometryData( 13.404954, 52.520008);
        var display = CreateDisplayData("Location");

        var request = new CreateLocationRequest
        {
            Geometry = geometry,
            Display = display
        };

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdAtResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var locationId = Assert.IsType<LocationResponse>(createdAtResult.Value);

        // Verify event was published
        var events = await _eventReader.GetEventsForAggregate(locationId.Id);
        var createdEvent = Assert.Single(events);
        var locationCreated = Assert.IsType<LocationCreated>(createdEvent);
        Assert.Equal(locationId.Id, locationCreated.LocationId);
        Assert.Equal(owner, locationCreated.OwnerId);

        // Verify read model was updated through event handler
        var readModel = await _read.GetById(locationId.Id);
        Assert.NotNull(readModel);
        Assert.Equal(owner, readModel.OwnerId);
        Assert.Equal(request.Display.Name, readModel.Display.Name);
        Assert.Equal(request.Geometry.Longitude, readModel.Coordinates.Longitude);
        Assert.Equal(request.Geometry.Latitude, readModel.Coordinates.Latitude);
    }

    [Fact]
    public async Task UpdatePosition_ShouldUpdateLocationAndReadModel()
    {
        // Arrange
        var owner = Guid.NewGuid();
        var locationId = await CreateTestLocation(owner, 13.404954, 52.520008, "Location");

        var newGeometry = CreateGeometryData(14.505065, 53.531119);
        var updateRequest = new UpdateLocationRequest
        {
            Geometry = newGeometry
        };

        // Act
        var result = await _controller.Update(locationId, updateRequest);

        // Assert
        Assert.IsType<ActionResult<LocationResponse>>(result);

        // Verify events
        var events = (await _eventReader.GetEventsForAggregate(locationId)).ToList();
        Assert.Equal(2, events.Count);
        var updateEvent = Assert.IsType<LocationUpdated>(events[1]);
        Assert.Equal(locationId, updateEvent.LocationId);

        // Verify read model was updated through event handler
        var readModel = await _read.GetById(locationId);
        Assert.NotNull(readModel);
        Assert.Equal(updateRequest.Geometry!.Longitude, readModel.Coordinates.Longitude);
        Assert.Equal(updateRequest.Geometry!.Latitude, readModel.Coordinates.Latitude);
        Assert.Equal("Location", readModel.Display.Name);
    }
    
    [Fact]
    public async Task UpdatePosition_ShouldUpdateDisplayInfoAndReadModel()
    {
        // Arrange
        var owner = Guid.NewGuid();
        var locationId = await CreateTestLocation(owner, 13.404954, 52.520008, "Location");

        var newDisplay = CreateDisplayUpdateData("New Name", "New Description", "New Icon");
        var updateRequest = new UpdateLocationRequest
        {
            Display = newDisplay
        };

        // Act
        var result = await _controller.Update(locationId, updateRequest);

        // Assert
        Assert.IsType<ActionResult<LocationResponse>>(result);

        // Verify events
        var events = (await _eventReader.GetEventsForAggregate(locationId)).ToList();
        Assert.Equal(2, events.Count);
        var updateEvent = Assert.IsType<LocationUpdated>(events[1]);
        Assert.Equal(locationId, updateEvent.LocationId);

        // Verify read model was updated through event handler
        var readModel = await _read.GetById(locationId);
        Assert.NotNull(readModel);
        Assert.Equal(newDisplay.Name, readModel.Display.Name);
        Assert.Equal(newDisplay.Description, readModel.Display.Description);
        Assert.Equal(newDisplay.Icon, readModel.Display.Icon);
    }

    [Fact]
    public async Task Delete_ShouldDeleteLocationAndUpdateReadModel()
    {
        // Arrange
        var owner = Guid.NewGuid();
        var locationId = await CreateTestLocation(owner, 13.404954, 52.520008, "Location");
        
        // Act
        var result = await _controller.Delete(locationId);

        // Assert
        Assert.IsType<NoContentResult>(result);

        // Verify events
        var events = (await _eventReader.GetEventsForAggregate(locationId)).ToList();
        Assert.Equal(2, events.Count);
        var deleteEvent = Assert.IsType<LocationDeleted>(events[1]);
        Assert.Equal(locationId, deleteEvent.LocationId);

        // Verify read model was deleted through event handler
        var readModel = await _read.GetById(locationId);
        Assert.Null(readModel);
    }

    [Fact]
    public async Task GetInExtent_ShouldReturnMatchingLocations()
    {
        // Arrange
        var owner = Guid.NewGuid();
        
        // Create Oslo location - outside the search extent
        await CreateTestLocation(owner, 13.404954, 53.520008, "Oslo");

        // Create Berlin location - inside the search extent
        var berlinId = await CreateTestLocation(owner, 13.404954, 52.520008, "Berlin");
        
        // Act - Query for Berlin area
        var result = await _controller.GetInExtent(
           
                    13.404,  
                   52.519, 
                    13.405, 
                    52.521 
        );

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var locations = Assert.IsAssignableFrom<LocationsResponse>(okResult.Value);
        
        var locationList = locations.Items;
        Assert.Single(locationList);
        Assert.Equal(berlinId, locationList[0].Id);
        Assert.Equal(13.404954, locationList[0].Geometry.Longitude);
        Assert.Equal(52.520008, locationList[0].Geometry.Latitude);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await ValueTask.CompletedTask;
    }
}