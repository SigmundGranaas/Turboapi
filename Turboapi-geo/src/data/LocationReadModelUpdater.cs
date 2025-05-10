using System.Diagnostics;
using NetTopologySuite.Geometries;
using Turboapi_geo.data.model;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;

public interface ILocationEventHandler<in TEvent> where TEvent : DomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}

public class LocationCreatedHandler : ILocationEventHandler<LocationCreated>
{
    private readonly ILocationWriteRepository _repo;
    private readonly ILogger<LocationCreatedHandler> _logger;
    private readonly ActivitySource _activitySource;

    public LocationCreatedHandler(
        ILocationWriteRepository repo,
        ILogger<LocationCreatedHandler> logger)
    {
        _repo = repo;
        _logger = logger;
        _activitySource = new ActivitySource("LocationCreatedHandler");
    }

    public async Task HandleAsync(LocationCreated @event, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("Handle Location Created");
        activity?.SetTag("location.id", @event.LocationId);

        try
        {
            var factory = new GeometryFactory();
            var entity = new LocationEntity
            {
                Id = @event.LocationId,
                OwnerId = @event.OwnerId,
                Geometry = @event.Coordinates.ToPoint(factory),
                Name = @event.Display.Name,
                Description = @event.Display.Description,
                Icon = @event.Display.Icon,
                CreatedAt = @event.OccurredAt,
            };

            await _repo.Add(entity);
            _logger.LogInformation("Created location {LocationId} for owner {OwnerId}",
                @event.LocationId, @event.OwnerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle LocationCreated event for {LocationId}",
                @event.LocationId);
            throw;
        }
    }
}

public class LocationUpdatedHandler : ILocationEventHandler<LocationUpdated>
{
    private readonly ILocationWriteRepository _repo;
    private readonly ILogger<LocationUpdatedHandler> _logger;
    private readonly ActivitySource _activitySource;

    public LocationUpdatedHandler(
        ILocationWriteRepository repo,
        ILogger<LocationUpdatedHandler> logger)
    {
        _repo = repo;
        _logger = logger;
        _activitySource = new ActivitySource("LocationUpdatedHandler");
    }

    public async Task HandleAsync(LocationUpdated @event, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("Handle location updated");
        activity?.SetTag("location.id", @event.LocationId);

        try
        {
            if (@event.Updates.HasAnyChange)
            {
                await _repo.UpdatePartial(
                    @event.LocationId,
                    @event.Updates.Coordinates,
                    @event.Updates.Display);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle LocationDisplayInformationChanged event for {LocationId}",
                @event.LocationId);
            throw;
        }
    }
}

public class LocationDeletedHandler : ILocationEventHandler<LocationDeleted>
{
    private readonly ILocationWriteRepository _repo;
    private readonly ILogger<LocationDeletedHandler> _logger;
    private readonly ActivitySource _activitySource;

    public LocationDeletedHandler(
        ILocationWriteRepository repo,
        ILogger<LocationDeletedHandler> logger)
    {
        _repo = repo;
        _logger = logger;
        _activitySource = new ActivitySource("LocationDeletedHandler");
    }

    public async Task HandleAsync(LocationDeleted @event, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("Handle Location Deleted");
        activity?.SetTag("location.id", @event.LocationId);

        try
        {
            var location = await _repo.GetById(@event.LocationId);
            if (location != null)
            {
                await _repo.Delete(location);
                _logger.LogInformation("Deleted location {LocationId}", @event.LocationId);
            }
            else
            {
                _logger.LogWarning("Location {LocationId} not found for deletion",
                    @event.LocationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle LocationDeleted event for {LocationId}",
                @event.LocationId);
            throw;
        }
    }
}