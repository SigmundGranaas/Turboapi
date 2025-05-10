using System.Diagnostics;
using Turboapi_geo.domain.events;

namespace Turboapi_geo.data
{
    public interface IDirectReadModelProjector
    {
        Task ProjectEventsAsync(IReadOnlyList<DomainEvent> events, CancellationToken cancellationToken = default);
    }

    public class DirectReadModelProjector : IDirectReadModelProjector
    {
        private readonly LocationCreatedHandler _locationCreatedHandler;
        private readonly LocationUpdatedHandler _locationUpdatedHandler;
        private readonly LocationDeletedHandler _locationDeletedHandler;
        private readonly ILogger<DirectReadModelProjector> _logger;
        private readonly ActivitySource _activitySource;

        public DirectReadModelProjector(
            LocationCreatedHandler locationCreatedHandler, // Inject concrete handler
            LocationUpdatedHandler locationUpdatedHandler, // Inject concrete handler
            LocationDeletedHandler locationDeletedHandler, // Inject concrete handler
            ILogger<DirectReadModelProjector> logger)
        {
            _locationCreatedHandler = locationCreatedHandler;
            _locationUpdatedHandler = locationUpdatedHandler;
            _locationDeletedHandler = locationDeletedHandler;
            _logger = logger;
            _activitySource = new ActivitySource(nameof(DirectReadModelProjector));
        }

        public async Task ProjectEventsAsync(IReadOnlyList<DomainEvent> events, CancellationToken cancellationToken = default)
        {
            if (events == null || !events.Any())
            {
                return;
            }

            foreach (var domainEvent in events)
            {
                try
                {
                    await ProjectSpecificEventAsync(domainEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error projecting event {EventId} of type {EventType} directly.", domainEvent.Id, domainEvent.EventType);
                    throw;
                }
            }
        }

        private async Task ProjectSpecificEventAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
        {
            using var activity = _activitySource.StartActivity($"Project {domainEvent.EventType}");
            activity?.SetTag("event.id", domainEvent.Id);
            activity?.SetTag("event.type", domainEvent.EventType);

            switch (domainEvent)
            {
                case LocationCreated createdEvent:
                    activity?.SetTag("location.id", createdEvent.LocationId);
                    await _locationCreatedHandler.HandleAsync(createdEvent, cancellationToken);
                    _logger.LogInformation("Directly projected LocationCreated for {LocationId} using LocationCreatedHandler", createdEvent.LocationId);
                    break;

                case LocationUpdated updatedEvent:
                    activity?.SetTag("location.id", updatedEvent.LocationId);
                    await _locationUpdatedHandler.HandleAsync(updatedEvent, cancellationToken);
                    _logger.LogInformation("Directly projected LocationUpdated for {LocationId} using LocationUpdatedHandler", updatedEvent.LocationId);
                    break;

                case LocationDeleted deletedEvent:
                    activity?.SetTag("location.id", deletedEvent.LocationId);
                    await _locationDeletedHandler.HandleAsync(deletedEvent, cancellationToken);
                    _logger.LogInformation("Directly projected LocationDeleted for {LocationId} using LocationDeletedHandler", deletedEvent.LocationId);
                    break;

                default:
                    _logger.LogWarning("Unhandled event type for direct projection: {EventType}", domainEvent.EventType);
                    break;
            }
        }
    }
}