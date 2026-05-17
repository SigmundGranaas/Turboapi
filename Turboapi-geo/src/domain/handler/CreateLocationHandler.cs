using Turbo.Outbox;
using Turboapi_geo.data;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.model;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;

namespace Turboapi_geo.domain.handler;

public class CreateLocationHandler
{
    private readonly IOutbox<LocationReadContext> _outbox;
    private readonly LocationReadContext _db;
    private readonly IDirectReadModelProjector _readModelHandler;

    public CreateLocationHandler(
        IOutbox<LocationReadContext> outbox,
        LocationReadContext db,
        IDirectReadModelProjector readModelHandler)
    {
        _outbox = outbox;
        _db = db;
        _readModelHandler = readModelHandler;
    }

    public async Task<Guid> Handle(CreateLocationCommand command)
    {
        var location = Location.Create(
            command.UserId,
            command.Coordinates,
            command.Display);

        await _readModelHandler.ProjectEventsAsync(location.Events);
        await _outbox.AppendGeoEventsAsync(location.Id, location.Events);
        await _db.SaveChangesAsync();
        return location.Id;
    }
}
