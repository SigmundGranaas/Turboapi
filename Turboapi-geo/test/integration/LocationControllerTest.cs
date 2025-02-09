using System.Security.Claims;
using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;
using Turboapi_geo.controller;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Xunit;

public class LocationsControllerTests : IAsyncDisposable
{
    private readonly LocationsController _controller;
    private readonly InMemoryLocationWriteRepository _writeRepository;
    private readonly InMemoryLocationReadModel _readModel;
    private readonly ITestMessageBus _messageBus;
    private readonly IEventWriter _eventWriter;
    private readonly IEventReader _eventReader;
    private readonly TestEventSubscriber _eventSubscriber;
    private readonly GeometryFactory _geometryFactory;
    private readonly LocationCreatedHandler _createdHandler;
    private readonly LocationPositionChangedHandler _positionChangedHandler;
    private readonly LocationDeletedHandler _deletedHandler;
    private readonly CancellationTokenSource _cts;

    public LocationsControllerTests()
    {
        // Initialize repositories
        var locationStore = new Dictionary<Guid, LocationReadEntity>();
        _writeRepository = new InMemoryLocationWriteRepository(locationStore);
        _readModel = new InMemoryLocationReadModel(locationStore);

        // Setup test event infrastructure
        _messageBus = new GeoSpatial.Tests.Doubles.TestMessageBus();
        _eventWriter = new TestEventWriter(_messageBus);
        _eventReader = new TestEventReader(_messageBus);
        _eventSubscriber = new TestEventSubscriber(_messageBus);
        
        // Setup geometry factory
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        
        // Initialize event handlers
        _createdHandler = new LocationCreatedHandler(
            _writeRepository, 
            new TestLogger<LocationCreatedHandler>());
            
        _positionChangedHandler = new LocationPositionChangedHandler(
            _writeRepository, 
            new TestLogger<LocationPositionChangedHandler>());
            
        _deletedHandler = new LocationDeletedHandler(
            _writeRepository, 
            new TestLogger<LocationDeletedHandler>());

        // Subscribe handlers to events
        _eventSubscriber.Subscribe<LocationCreated>(evt => _createdHandler.HandleAsync(evt, _cts.Token));
        _eventSubscriber.Subscribe<LocationPositionChanged>(evt => _positionChangedHandler.HandleAsync(evt, _cts.Token));
        _eventSubscriber.Subscribe<LocationDeleted>(evt => _deletedHandler.HandleAsync(evt, _cts.Token));
        
        // Initialize command handlers
        var createHandler = new CreateLocationHandler(_eventWriter, _geometryFactory);
        var updateHandler = new UpdateLocationPositionHandler(_readModel, _eventWriter, _geometryFactory);
        var deleteHandler = new DeleteLocationHandler(_eventWriter, _readModel);
        var getByIdHandler = new GetLocationByIdHandler(_readModel);
        var getInExtentHandler = new GetLocationsInExtentHandler(_readModel);

        // Initialize controller
        _controller = new LocationsController(
            createHandler,
            updateHandler,
            deleteHandler,
            getByIdHandler,
            getInExtentHandler
        );
        
        _cts = new CancellationTokenSource();
    }

    private void SetupControllerContext(string userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
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

    [Fact]
    public async Task Create_ShouldCreateLocationAndUpdateReadModel()
    {
        // Arrange
        var owner = Guid.NewGuid();
        SetupControllerContext(owner.ToString());
        
        var request = new CreateLocationRequest(
            13.404954,
            52.520008
        );

        // Act
        var result = await _controller.Create(request);

        // Assert
        var createdAtResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var locationId = Assert.IsType<CreateLocationResponse>(createdAtResult.Value);

        // Verify event was published
        var events = await _eventReader.GetEventsForAggregate(locationId.Id);
        var createdEvent = Assert.Single(events);
        var locationCreated = Assert.IsType<LocationCreated>(createdEvent);
        Assert.Equal(locationId.Id, locationCreated.LocationId);
        Assert.Equal(owner.ToString(), locationCreated.OwnerId);

        // Verify read model was updated through event handler
        var readModel = await _readModel.GetById(locationId.Id);
        Assert.NotNull(readModel);
        Assert.Equal(owner.ToString(), readModel.OwnerId);
        Assert.Equal(request.Longitude, readModel.Geometry.X);
        Assert.Equal(request.Latitude, readModel.Geometry.Y);
    }

    [Fact]
    public async Task UpdatePosition_ShouldUpdateLocationAndReadModel()
    {
        // Arrange
        var owner = Guid.NewGuid();
        SetupControllerContext(owner.ToString());

        // Create initial location
        var createRequest = new CreateLocationRequest(
            13.404954,
            52.520008
        );
        var createResult = await _controller.Create(createRequest);
        var createdAtResult = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var locationId = Assert.IsType<CreateLocationResponse>(createdAtResult.Value);

        var updateRequest = new UpdateLocationPositionRequest(
            13.405,
            52.520
        );

        // Act
        var result = await _controller.UpdatePosition(locationId.Id, updateRequest);

        // Assert
        Assert.IsType<NoContentResult>(result);

        // Verify events
        var events = (await _eventReader.GetEventsForAggregate(locationId.Id)).ToList();
        Assert.Equal(2, events.Count);
        var updateEvent = Assert.IsType<LocationPositionChanged>(events[1]);
        Assert.Equal(locationId.Id, updateEvent.LocationId);

        // Verify read model was updated through event handler
        var readModel = await _readModel.GetById(locationId.Id);
        Assert.NotNull(readModel);
        Assert.Equal(updateRequest.Longitude, readModel.Geometry.X);
        Assert.Equal(updateRequest.Latitude, readModel.Geometry.Y);
    }

    [Fact]
    public async Task Delete_ShouldDeleteLocationAndUpdateReadModel()
    {
        // Arrange
        var owner = Guid.NewGuid();
        SetupControllerContext(owner.ToString());

        // Create initial location
        var createRequest = new CreateLocationRequest(
            13.404954,
            52.520008
        );
        var createResult = await _controller.Create(createRequest);
        var createdAtResult = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var locationId = Assert.IsType<CreateLocationResponse>(createdAtResult.Value);
        
        // Act
        var result = await _controller.Delete(locationId.Id);

        // Assert
        Assert.IsType<NoContentResult>(result);

        // Verify events
        var events = (await _eventReader.GetEventsForAggregate(locationId.Id)).ToList();
        Assert.Equal(2, events.Count);
        var deleteEvent = Assert.IsType<LocationDeleted>(events[1]);
        Assert.Equal(locationId.Id, deleteEvent.LocationId);

        // Verify read model was deleted through event handler
        var readModel = await _readModel.GetById(locationId.Id);
        Assert.Null(readModel);
    }

    [Fact]
    public async Task GetInExtent_ShouldReturnMatchingLocations()
    {
        // Arrange
        var owner = Guid.NewGuid().ToString();
        SetupControllerContext(owner);

        // Create locations in Oslo and Berlin
        var oslo = new CreateLocationRequest(
            10.757933,
            59.911491
        );
        await _controller.Create(oslo);

        var berlin = new CreateLocationRequest(
            13.404954,
            52.520008
        );
        
        var createBerlin = await _controller.Create(berlin);
        var berl = Assert.IsType<CreatedAtActionResult>(createBerlin.Result);
        var locationId = Assert.IsType<CreateLocationResponse>(berl.Value);
        var berlinId = locationId.Id;
        
        // Act - Query for Berlin area
        var result = await _controller.GetInExtent(
            13.404,
            52.519,
            13.405,
            52.521
        );

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var locations = Assert.IsAssignableFrom<IEnumerable<LocationResponse>>(okResult.Value);
        
        var locationList = locations.ToList();
        Assert.Single(locationList);
        Assert.Equal(berlinId, locationList[0].Id);
        Assert.Equal(berlin.Longitude, locationList[0].Longitude);
        Assert.Equal(berlin.Latitude, locationList[0].Latitude);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await ValueTask.CompletedTask;
    }
}