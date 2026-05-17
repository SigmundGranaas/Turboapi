using Turbo.Outbox;
using Turboapi_geo.data;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;

namespace Turboapi_geo.domain.handler;

public class UpdateLocationHandler
{
    private readonly ILocationReadRepository _repository;
    private readonly IOutbox<LocationReadContext> _outbox;
    private readonly LocationReadContext _db;
    private readonly IDirectReadModelProjector _readModelHandler;

    public UpdateLocationHandler(
        ILocationReadRepository repository,
        IOutbox<LocationReadContext> outbox,
        LocationReadContext db,
        IDirectReadModelProjector readModelHandler)
    {
        _repository = repository;
        _outbox = outbox;
        _db = db;
        _readModelHandler = readModelHandler;
    }

    public async Task Handle(UpdateLocationCommand command)
    {
        var location = await _repository.GetById(command.LocationId);
        if (location == null)
            throw new LocationNotFoundException(command.LocationId.ToString());

        location.Update(command.UserId, command.Updates);

        await _readModelHandler.ProjectEventsAsync(location.Events);
        await _outbox.AppendGeoEventsAsync(location.Id, location.Events);
        await _db.SaveChangesAsync();
    }
}
