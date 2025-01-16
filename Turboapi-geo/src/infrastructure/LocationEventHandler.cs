using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;

namespace Turboapi_geo.infrastructure;

public class LocationEventHandler
{
    private readonly ILocationWriteRepository _writeRepository;
    private readonly ILogger<LocationEventHandler> _logger;

    public LocationEventHandler(
        ILocationWriteRepository writeRepository,
        ILogger<LocationEventHandler> logger)
    {
        _writeRepository = writeRepository;
        _logger = logger;
    }

    public async Task HandleLocationCreated(LocationCreated @event)
    {
        try
        {
            var location = new LocationReadEntity
            {
                Id = @event.LocationId,
                OwnerId = @event.OwnerId,
                Geometry = @event.Geometry,
            };

            await _writeRepository.Add(location);
            _logger.LogInformation("Location {LocationId} created", @event.LocationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling LocationCreated event for {LocationId}", @event.LocationId);
            throw;
        }
    }

    public async Task HandleLocationPositionChanged(LocationPositionChanged @event)
    {
        try
        {
            var location = await _writeRepository.GetById(@event.LocationId);
            if (location == null)
            {
                _logger.LogWarning("Location {LocationId} not found for position update", @event.LocationId);
                return;
            }

            location.Geometry = @event.Geometry;

            await _writeRepository.Update(location);
            _logger.LogInformation("Location {LocationId} position updated", @event.LocationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling LocationPositionChanged event for {LocationId}", @event.LocationId);
            throw;
        }
    }

    public async Task HandleLocationDeleted(LocationDeleted @event)
    {
        try
        {
            var location = await _writeRepository.GetById(@event.LocationId);
            if (location == null)
            {
                _logger.LogWarning("Location {LocationId} not found for deletion", @event.LocationId);
                return;
            }

            await _writeRepository.Delete(location);
            _logger.LogInformation("Location {LocationId} deleted", @event.LocationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling LocationDeleted event for {LocationId}", @event.LocationId);
            throw;
        }
    }
}