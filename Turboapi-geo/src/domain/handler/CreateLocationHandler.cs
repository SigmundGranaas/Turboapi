using GeoSpatial.Domain.Events;
using Turboapi_geo.data;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.model;

namespace Turboapi_geo.domain.handler;

public class CreateLocationHandler
{
    private readonly IEventWriter _eventWriter;
    private readonly IDirectReadModelProjector _readModelHandler;

    public CreateLocationHandler(IEventWriter eventWriter, IDirectReadModelProjector readModelHandler)
    {
        _eventWriter = eventWriter;
        _readModelHandler = readModelHandler;
    }

    public async Task<Guid> Handle(CreateLocationCommand command)
    {
        var location = Location.Create(
            command.UserId, 
            command.Coordinates, 
            command.Display);
                
        
        await _readModelHandler.ProjectEventsAsync(location.Events);
        await _eventWriter.AppendEvents(location.Events);
        return location.Id;
    }
}