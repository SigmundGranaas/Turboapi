using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using Medo;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;
using Turboapi_geo.controller;
using Turboapi_geo.data;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Xunit;

public class LocationsControllerTests
{
    private readonly LocationsController _controller;
    private readonly InMemoryLocationWriteRepository _writeRepository;
    private readonly InMemoryLocationReadModel _readModel;
    private readonly ITestMessageBus _messageBus;
    private readonly IEventWriter _eventWriter;
    private readonly IEventReader _eventReader;
    private readonly TestEventSubscriber _eventSubscriber;
    private readonly GeometryFactory _geometryFactory;
    private readonly LocationReadModelUpdater _locationReadModelUpdater;
    private readonly GetLocationByIdHandler _locationByIdQuery;
    private readonly GetLocationsInExtentHandler _locationsByIdQuery;
    private readonly CancellationTokenSource _cts;


    // Handlers
    private readonly CreateLocationHandler _createHandler;
    private readonly UpdateLocationPositionHandler _updateHandler;
    private readonly DeleteLocationHandler _deleteHandler;

    public LocationsControllerTests()
    {
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var dict = new Dictionary<Guid, LocationReadEntity>();
        _writeRepository = new InMemoryLocationWriteRepository(dict);
        _readModel = new InMemoryLocationReadModel(dict);

        // Setup event infrastructure
        _messageBus = new TestMessageBus();
        _eventWriter = new TestEventWriter(_messageBus);
        _eventReader = new TestEventReader(_messageBus);
        _eventSubscriber = new TestEventSubscriber(_messageBus);
        _cts = new CancellationTokenSource();

        // Initialize read model updater
        _locationReadModelUpdater = new LocationReadModelUpdater(_writeRepository, _eventSubscriber);

        // Initialize handlers with new event infrastructure
        _createHandler = new CreateLocationHandler(_eventWriter, _geometryFactory);
        _updateHandler = new UpdateLocationPositionHandler(_readModel, _eventWriter, _geometryFactory);
        _deleteHandler = new DeleteLocationHandler(_eventWriter, _readModel);

        _locationByIdQuery = new GetLocationByIdHandler(_readModel);
        _locationsByIdQuery = new GetLocationsInExtentHandler(_readModel);

        // Initialize controller
        _controller = new LocationsController(
            _createHandler,
            _updateHandler,
            _deleteHandler,
            _locationByIdQuery,
            _locationsByIdQuery
        );
    }
    
    private async Task InitializeAsync()
    {
        await _locationReadModelUpdater.StartAsync(_cts.Token);
    }

    [Fact]
    public async Task Create_ShouldCreateLocationAndUpdateReadModel()
    {
        await InitializeAsync();
        var owner = Uuid7.NewUuid7();
        // Arrange
        var request = new CreateLocationRequest(
            owner.ToString(),
            13.404954,
            52.520008
        );

        // Act
        var result = await _controller.Create(request);

        // Give time for event processing
        await Task.Delay(100);

        // Assert
        var createdAtResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var locationId = Assert.IsType<Guid>(createdAtResult.Value);

        // Verify domain model
        var location = await _writeRepository.GetById(locationId);
        Assert.NotNull(location);
        Assert.Equal(request.OwnerId, location.OwnerId);
        Assert.Equal(request.Longitude, location.Geometry.X);
        Assert.Equal(request.Latitude, location.Geometry.Y);

        // Verify event was published
        var events = await _eventReader.GetEventsForAggregate(locationId);
        var createdEvent = Assert.Single(events);
        var locationCreated = Assert.IsType<LocationCreated>(createdEvent);
        Assert.Equal(locationId, locationCreated.LocationId);
        Assert.Equal(request.OwnerId, locationCreated.OwnerId);

        // Verify read model was updated
        var readModel = await _readModel.GetById(locationId);
        Assert.NotNull(readModel);
        Assert.Equal(request.OwnerId, readModel.OwnerId);
        Assert.Equal(request.Longitude, readModel.Geometry.X);
        Assert.Equal(request.Latitude, readModel.Geometry.Y);
    }

    [Fact]
    public async Task UpdatePosition_ShouldUpdateLocationAndReadModel()
    {
        await InitializeAsync();

        
        var owner = Uuid7.NewUuid7();

        // Arrange - Create a location first
        var createRequest = new CreateLocationRequest(
            owner.ToString(),
            13.404954,
            52.520008
        );
        var createResult = await _controller.Create(createRequest);
        await Task.Delay(100); // Allow event processing
        
        var locationId = (Guid)((CreatedAtActionResult)createResult.Result).Value;

        var updateRequest = new UpdateLocationPositionRequest(
            13.405,
            52.520
        );

        // Act
        var result = await _controller.UpdatePosition(locationId, updateRequest);
        await Task.Delay(100); // Allow event processing

        // Assert
        Assert.IsType<NoContentResult>(result);

        // Verify domain model
        var location = await _writeRepository.GetById(locationId);
        Assert.NotNull(location);
        Assert.Equal(updateRequest.Longitude, location.Geometry.X);
        Assert.Equal(updateRequest.Latitude, location.Geometry.Y);

        // Verify events
        var events = (await _eventReader.GetEventsForAggregate(locationId)).ToList();
        Assert.Equal(2, events.Count);
        var updateEvent = events[1];
        var positionChanged = Assert.IsType<LocationPositionChanged>(updateEvent);
        Assert.Equal(locationId, positionChanged.LocationId);

        // Verify read model was updated
        var readModel = await _readModel.GetById(locationId);
        Assert.NotNull(readModel);
        Assert.Equal(updateRequest.Longitude, readModel.Geometry.X);
        Assert.Equal(updateRequest.Latitude, readModel.Geometry.Y);
    }

    [Fact]
    public async Task Delete_ShouldMarkAsDeletedAndUpdateReadModel()
    { 
        await InitializeAsync();

        var owner = Uuid7.NewUuid7();

        // Arrange - Create a location first
        var createRequest = new CreateLocationRequest(
            owner.ToString(),
            13.404954,
            52.520008
        );
        var createResult = await _controller.Create(createRequest);
        await Task.Delay(100); // Allow event processing
        
        var locationId = (Guid)((CreatedAtActionResult)createResult.Result).Value;
        // Act
        var result = await _controller.Delete(locationId);
        await Task.Delay(100); // Allow event processing
        
        // Verify domain model
        var location = await _writeRepository.GetById(locationId);
        Assert.Null(location);

        // Verify events
        var events = (await _eventReader.GetEventsForAggregate(locationId)).ToList();
        Assert.Equal(2, events.Count);
        var deleteEvent = events[1];
        var locationDeleted = Assert.IsType<LocationDeleted>(deleteEvent);
        Assert.Equal(locationId, locationDeleted.LocationId);

        // Verify read model was updated
        var readModel = await _readModel.GetById(locationId);
        Assert.Null(readModel);
    }

    [Fact]
    public async Task GetInExtent_ShouldReturnMatchingLocations()
    {
        await InitializeAsync();

        var owner = Uuid7.NewUuid7().ToString();

        // Arrange - Create two locations
        var oslo = new CreateLocationRequest(
            owner,
            10.757933,
            59.911491
        );
        var guid = Guid.NewGuid().ToString();

        await _controller.Create(oslo);
        await Task.Delay(100); // Allow event processing

        var berlin = new CreateLocationRequest(
            guid,
            13.404954,
            52.520008
        );
        var createBerlin = await _controller.Create(berlin);
        await Task.Delay(100); // Allow event processing
        
        var berlinId = (Guid)((CreatedAtActionResult)createBerlin.Result).Value;
        
        // Act - Query for Berlin area
        var result = await _controller.GetInExtent(
            guid,
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
}