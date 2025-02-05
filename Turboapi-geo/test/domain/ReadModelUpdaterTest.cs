using GeoSpatial.Tests.Doubles;

using NetTopologySuite.Geometries;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;
using Xunit;

public class LocationEventHandlerTests : IAsyncDisposable
{
    private readonly IServiceScope _scope;
    private readonly ILocationWriteRepository _writer;
    private readonly LocationCreatedHandler _createdHandler;
    private readonly LocationPositionChangedHandler _positionChangedHandler;
    private readonly LocationDeletedHandler _deletedHandler;
    private readonly CancellationTokenSource _cts;

    public LocationEventHandlerTests()
    {
        // Setup service collection
        var services = new ServiceCollection();
        
        // Register dependencies
        var writer = new InMemoryLocationWriteRepository(new Dictionary<Guid, LocationReadEntity>());
        services.AddSingleton<ILocationWriteRepository>(writer);
        services.AddSingleton<ILogger<LocationCreatedHandler>>(new TestLogger<LocationCreatedHandler>());
        services.AddSingleton<ILogger<LocationPositionChangedHandler>>(new TestLogger<LocationPositionChangedHandler>());
        services.AddSingleton<ILogger<LocationDeletedHandler>>(new TestLogger<LocationDeletedHandler>());
        
        services.AddScoped<LocationCreatedHandler>();
        services.AddScoped<LocationPositionChangedHandler>();
        services.AddScoped<LocationDeletedHandler>();

        var provider = services.BuildServiceProvider();
        _scope = provider.CreateScope();

        // Get instances
        _writer = _scope.ServiceProvider.GetRequiredService<ILocationWriteRepository>();
        _createdHandler = _scope.ServiceProvider.GetRequiredService<LocationCreatedHandler>();
        _positionChangedHandler = _scope.ServiceProvider.GetRequiredService<LocationPositionChangedHandler>();
        _deletedHandler = _scope.ServiceProvider.GetRequiredService<LocationDeletedHandler>();
        
        _cts = new CancellationTokenSource();
    }

    [Fact]
    public async Task WhenLocationCreated_ShouldAddToReadModel()
    {
        // Arrange
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var @event = new LocationCreated(
            Guid.NewGuid(),
            "owner123",
            geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );

        // Act
        await _createdHandler.HandleAsync(@event, _cts.Token);

        // Assert
        var location = await _writer.GetById(@event.LocationId);
        Assert.NotNull(location);
        Assert.Equal(@event.LocationId, location.Id);
        Assert.Equal(@event.Geometry, location.Geometry);
        Assert.Equal(@event.OwnerId, location.OwnerId);
        Assert.Equal(@event.OccurredAt, location.CreatedAt);
        Assert.False(location.IsDeleted);
    }

    [Fact]
    public async Task WhenLocationPositionChanged_ShouldUpdateGeometry()
    {
        // Arrange
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var locationId = Guid.NewGuid();
        
        var createEvent = new LocationCreated(
            locationId,
            "owner123",
            geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );
        await _createdHandler.HandleAsync(createEvent, _cts.Token);

        var updateEvent = new LocationPositionChanged(
            locationId,
            geometryFactory.CreatePoint(new Coordinate(13.405, 52.520))
        );

        // Act
        await _positionChangedHandler.HandleAsync(updateEvent, _cts.Token);

        // Assert
        var location = await _writer.GetById(locationId);
        Assert.NotNull(location);
        Assert.Equal(updateEvent.Geometry, location.Geometry);
        Assert.False(location.IsDeleted);
    }

    [Fact]
    public async Task WhenLocationDeleted_ShouldDeleteLocation()
    {
        // Arrange
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var locationId = Guid.NewGuid();
        
        var createEvent = new LocationCreated(
            locationId,
            "owner123",
            geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );
        await _createdHandler.HandleAsync(createEvent, _cts.Token);

        var deleteEvent = new LocationDeleted(locationId, "owner123");

        // Act
        await _deletedHandler.HandleAsync(deleteEvent, _cts.Token);

        // Assert
        var location = await _writer.GetById(locationId);
        Assert.Null(location);
    }

    [Fact]
    public async Task WhenUpdatingNonExistentLocation_ShouldNotThrowException()
    {
        // Arrange
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var locationId = Guid.NewGuid();
        
        var updateEvent = new LocationPositionChanged(
            locationId,
            geometryFactory.CreatePoint(new Coordinate(13.405, 52.520))
        );

        // Act & Assert
        await _positionChangedHandler.HandleAsync(updateEvent, _cts.Token);
        // Should not throw
    }

    [Fact]
    public async Task WhenDeletingNonExistentLocation_ShouldNotThrowException()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var deleteEvent = new LocationDeleted(locationId, "owner123");

        // Act & Assert
        await _deletedHandler.HandleAsync(deleteEvent, _cts.Token);
        // Should not throw
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        _scope.Dispose();
        await ValueTask.CompletedTask;
    }
}

// Test Logger implementation
public class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}