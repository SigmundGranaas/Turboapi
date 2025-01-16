using GeoSpatial.Domain.Events;
using GeoSpatial.Tests.Doubles;
using NetTopologySuite.Geometries;
using Turboapi_geo.data;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;
using Xunit;


public class LocationReadModelUpdaterTests : IAsyncDisposable
{
    private readonly ITestMessageBus _messageBus;
    private readonly IEventWriter _eventWriter;
    private readonly IEventSubscriber _eventSubscriber;
    private readonly ILocationWriteRepository _writer;
    private readonly LocationReadModelUpdater _updater;
    private readonly CancellationTokenSource _cts;

    public LocationReadModelUpdaterTests()
    {
        _messageBus = new TestMessageBus();
        _eventWriter = new TestEventWriter(_messageBus);
        _eventSubscriber = new TestEventSubscriber(_messageBus);
        _writer = new InMemoryLocationWriteRepository(new Dictionary<Guid, LocationReadEntity>());
        _updater = new LocationReadModelUpdater(_writer, _eventSubscriber);
        _cts = new CancellationTokenSource();
    }

    private async Task InitializeAsync()
    {
        await _updater.StartAsync(_cts.Token);
    }

    [Fact]
    public async Task WhenLocationCreated_ShouldAddToReadModel()
    {
        // Arrange
        await InitializeAsync();
        
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
        var @event = new LocationCreated(
            Guid.NewGuid(),
            "owner123",
            geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );

        // Act
        await _eventWriter.AppendEvents(new[] { @event });

        // Give handlers time to process
        await Task.Delay(100);

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
        await InitializeAsync();
        
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
        var locationId = Guid.NewGuid();
        
        var createEvent = new LocationCreated(
            locationId,
            "owner123",
            geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );
        await _eventWriter.AppendEvents(new[] { createEvent });
        await Task.Delay(100);

        var updateEvent = new LocationPositionChanged(
            locationId,
            geometryFactory.CreatePoint(new Coordinate(13.405, 52.520))
        );

        // Act
        await _eventWriter.AppendEvents(new[] { updateEvent });
        await Task.Delay(100);

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
        await InitializeAsync();
        
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
        var locationId = Guid.NewGuid();
        
        var createEvent = new LocationCreated(
            locationId,
            "owner123",
            geometryFactory.CreatePoint(new Coordinate(13.404954, 52.520008))
        );
        await _eventWriter.AppendEvents(new[] { createEvent });
        await Task.Delay(100);

        var deleteEvent = new LocationDeleted(locationId, "owner123");

        // Act
        await _eventWriter.AppendEvents(new[] { deleteEvent });
        await Task.Delay(100);

        // Assert
        var location = await _writer.GetById(locationId);
        Assert.Null(location);
    }

    [Fact]
    public async Task WhenUpdatingNonExistentLocation_ShouldNotThrowException()
    {
        // Arrange
        await InitializeAsync();
        
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
        var locationId = Guid.NewGuid();
        
        var updateEvent = new LocationPositionChanged(
            locationId,
            geometryFactory.CreatePoint(new Coordinate(13.405, 52.520))
        );

        // Act & Assert
        await _eventWriter.AppendEvents(new[] { updateEvent });
        await Task.Delay(100); // Should not throw
    }

    [Fact]
    public async Task WhenDeletingNonExistentLocation_ShouldNotThrowException()
    {
        // Arrange
        await InitializeAsync();
        
        var locationId = Guid.NewGuid();
        var deleteEvent = new LocationDeleted(locationId, "owner123");

        // Act & Assert
        await _eventWriter.AppendEvents(new[] { deleteEvent });
        await Task.Delay(100); // Should not throw
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _updater.StopAsync(_cts.Token);
        }
        finally
        {
            _cts.Cancel();
            _cts.Dispose();
            _updater.Dispose();
        }
    }
}