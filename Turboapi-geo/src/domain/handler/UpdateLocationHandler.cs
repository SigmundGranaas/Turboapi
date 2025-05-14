using GeoSpatial.Domain.Events;
using Turboapi_geo.data;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;

namespace Turboapi_geo.domain.handler;

public class UpdateLocationHandler
{
    private readonly ILocationReadRepository _repository;
    private readonly IEventWriter _eventStore;
    private readonly IDirectReadModelProjector _readModelHandler;

    public UpdateLocationHandler(
        ILocationReadRepository repository,
        IEventWriter eventStore,
        IDirectReadModelProjector _readModelHandler)
    {
        _repository = repository;
        _eventStore = eventStore;
        this._readModelHandler = _readModelHandler;
    }

    public async Task Handle(UpdateLocationCommand command)
    {
        var location = await _repository.GetById(command.LocationId);
        if (location == null)
            throw new LocationNotFoundException(command.LocationId.ToString());
        
        location.Update(command.UserId, command.Updates);
        
        
        await _readModelHandler.ProjectEventsAsync(location.Events);
        await _eventStore.AppendEvents(location.Events);
    }
}