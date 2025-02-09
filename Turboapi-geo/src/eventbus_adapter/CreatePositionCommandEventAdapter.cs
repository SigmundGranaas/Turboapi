using System.Diagnostics;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.handler;

namespace Turboapi_geo.eventbus_adapter;


public class CreatePositionCommandEventAdapter : ILocationEventHandler<CreatePositionEvent>
{
    private readonly CreateLocationHandler _createHandler;   
    private readonly ILogger<LocationCreatedHandler> _logger;
    private readonly ActivitySource _activitySource;

    public CreatePositionCommandEventAdapter(
        CreateLocationHandler handler,
        ILogger<LocationCreatedHandler> logger)
    {
        _createHandler = handler;
        _logger = logger;
        _activitySource = new ActivitySource("CreatePositionCommandEventHandler");
    }

    public async Task HandleAsync(CreatePositionEvent @event, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("Handle create position event command");
        activity?.SetTag("location.id", @event.positionId);

        var command = new Commands.CreatePredefinedLocationCommand(
            @event.positionId,
            @event.ownerId,
            @event.position.Longitude,
            @event.position.Latitude
        );
        
        await _createHandler.Handle(command);
    }
}
