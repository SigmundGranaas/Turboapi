using Turbo.Outbox;
using Turboapi_geo.data;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;

namespace Turboapi_geo.domain.handler;

public class DeleteLocationHandler
{
    private readonly IOutbox _outbox;
    private readonly LocationReadContext _db;
    private readonly ILocationReadRepository _locationReadRepository;
    private readonly IDirectReadModelProjector _readModelHandler;

    public DeleteLocationHandler(
        IOutbox outbox,
        LocationReadContext db,
        ILocationReadRepository locationReadRepository,
        IDirectReadModelProjector readModelHandler)
    {
        _outbox = outbox;
        _db = db;
        _locationReadRepository = locationReadRepository;
        _readModelHandler = readModelHandler;
    }

    public async Task Handle(DeleteLocationCommand command)
    {
        var location = await _locationReadRepository.GetById(command.LocationId);
        if (location == null)
            throw new LocationNotFoundException($"Location with ID {command.LocationId} not found");

        location.Delete(command.UserId);

        await _readModelHandler.ProjectEventsAsync(location.Events);
        await _outbox.AppendGeoEventsAsync(location.Id, location.Events);
        await _db.SaveChangesAsync();
    }
}
