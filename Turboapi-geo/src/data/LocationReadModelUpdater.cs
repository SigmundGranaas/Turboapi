using System.Diagnostics;
using GeoSpatial.Domain.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;

namespace Turboapi_geo.data;

public class LocationReadModelUpdater : IHostedService, IDisposable
{
    private readonly ILocationWriteRepository _repo;
    private readonly IEventSubscriber _eventSubscriber;
    private readonly ILogger<LocationReadModelUpdater> _logger;
    private readonly ActivitySource _activitySource;
    private bool _isStarted;
    
    public LocationReadModelUpdater(
        ILocationWriteRepository repo,
        IEventSubscriber eventSubscriber,
        ILogger<LocationReadModelUpdater>? logger = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _eventSubscriber = eventSubscriber ?? throw new ArgumentNullException(nameof(eventSubscriber));
        _logger = logger ?? NullLogger<LocationReadModelUpdater>.Instance;
        _activitySource = new ActivitySource("LocationReadModelUpdater");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting LocationReadModelUpdater");
        
        try
        {
            _eventSubscriber.Subscribe<LocationCreated>(HandleLocationCreated);
            _eventSubscriber.Subscribe<LocationPositionChanged>(HandleLocationPositionChanged);
            _eventSubscriber.Subscribe<LocationDeleted>(HandleLocationDeleted);
            
            _isStarted = true;
            _logger.LogInformation("Successfully subscribed to all location events");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start LocationReadModelUpdater");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_isStarted)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Stopping LocationReadModelUpdater");
        
        try
        {
            UnsubscribeFromEvents();
            _isStarted = false;
            _logger.LogInformation("Successfully unsubscribed from all location events");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping LocationReadModelUpdater");
            throw;
        }

        return Task.CompletedTask;
    }

    private async Task HandleLocationCreated(LocationCreated @event)
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
                IsDeleted = false,
                CreatedAt = @event.OccurredAt
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

    private async Task HandleLocationPositionChanged(LocationPositionChanged @event)
    {
        using var activity = _activitySource.StartActivity("Handle Location Position Changed");
        activity?.SetTag("location.id", @event.LocationId);

        try
        {
            var location = await _repo.GetById(@event.LocationId);
            if (location != null)
            {
                location.Geometry = @event.Geometry;
                await _repo.Update(location);
                _logger.LogInformation("Updated position for location {LocationId}", @event.LocationId);
            }
            else
            {
                _logger.LogWarning("Location {LocationId} not found for position update", 
                    @event.LocationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle LocationPositionChanged event for {LocationId}", 
                @event.LocationId);
            throw;
        }
    }

    private async Task HandleLocationDeleted(LocationDeleted @event)
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

    private void UnsubscribeFromEvents()
    {
        _eventSubscriber.Unsubscribe<LocationCreated>(HandleLocationCreated);
        _eventSubscriber.Unsubscribe<LocationPositionChanged>(HandleLocationPositionChanged);
        _eventSubscriber.Unsubscribe<LocationDeleted>(HandleLocationDeleted);
    }

    public void Dispose()
    {
        if (_isStarted)
        {
            try
            {
                UnsubscribeFromEvents();
                _logger.LogInformation("Disposed LocationReadModelUpdater");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during LocationReadModelUpdater disposal");
            }
        }
    }
}