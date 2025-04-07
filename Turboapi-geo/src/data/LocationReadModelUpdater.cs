using System.Diagnostics;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.domain.value;

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
            var entity = new LocationReadEntity
            {
                Id = @event.LocationId,
                OwnerId = @event.OwnerId,
                Geometry = @event.Geometry,
                Name = @event.DisplayInformation.Name,
                Description = @event.DisplayInformation.Description,
                Icon = @event.DisplayInformation.Icon,
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

public class LocationPositionChangedHandler : ILocationEventHandler<LocationPositionChanged>
{
    private readonly ILocationWriteRepository _repo;
    private readonly ILogger<LocationPositionChangedHandler> _logger;
    private readonly ActivitySource _activitySource;

    public LocationPositionChangedHandler(
        ILocationWriteRepository repo,
        ILogger<LocationPositionChangedHandler> logger)
    {
        _repo = repo;
        _logger = logger;
        _activitySource = new ActivitySource("LocationPositionChangedHandler");
    }

    public async Task HandleAsync(LocationPositionChanged @event, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("Handle Location Position Changed");
        activity?.SetTag("location.id", @event.LocationId);

        try
        {
            // Use partial update instead of read-modify-write pattern
            await _repo.UpdatePosition(@event.LocationId, @event.Geometry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle LocationPositionChanged event for {LocationId}",
                @event.LocationId);
            throw;
        }
    }
}

public class LocationDisplayInformationChangedHandler : ILocationEventHandler<LocationDisplayInformationChanged>
{
    private readonly ILocationWriteRepository _repo;
    private readonly ILogger<LocationDisplayInformationChangedHandler> _logger;
    private readonly ActivitySource _activitySource;

    public LocationDisplayInformationChangedHandler(
        ILocationWriteRepository repo,
        ILogger<LocationDisplayInformationChangedHandler> logger)
    {
        _repo = repo;
        _logger = logger;
        _activitySource = new ActivitySource("LocationDisplayInformationChangedHandler");
    }

    public async Task HandleAsync(LocationDisplayInformationChanged @event, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("Handle Display information changed event");
        activity?.SetTag("location.id", @event.LocationId);

        try
        {
            // Use partial update instead of read-modify-write pattern
            await _repo.UpdateDisplayInformation(
                @event.LocationId,
                @event.Name,
                @event.Description,
                @event.Icon);
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