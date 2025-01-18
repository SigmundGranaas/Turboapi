using GeoSpatial.Domain.Events;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;

namespace Turboapi_geo.domain.handler;

public class DeleteLocationHandler
{
    private readonly ILocationReadModelRepository _repository;
    private readonly IEventWriter _eventStore;

    public DeleteLocationHandler(
        IEventWriter eventStore,
        ILocationReadModelRepository repository
        )
    {
        _repository = repository;
        _eventStore = eventStore;
    }

    public async Task Handle(Commands.DeleteLocationCommand command)
    {
        var location = await _repository.GetById(command.LocationId);
        if (location == null)
            throw new LocationNotFoundException(command.LocationId.ToString());

        if (location.OwnerId != command.OwnerId.ToString())
        {
            throw new UnauthorizedException("Only the owner is allowed to delete the location");
        }
        
        location.Delete();
        _eventStore.AppendEvents(location.Events);
    }
}