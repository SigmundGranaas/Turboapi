using Turbo.Outbox;
using Turbo.Outbox.Postgres;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.model;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;

namespace Turboapi_geo.domain.handler;

public class CreateLocationHandler
{
    private readonly IOutbox<LocationReadContext> _outbox;
    private readonly LocationReadContext _db;

    public CreateLocationHandler(
        IOutbox<LocationReadContext> outbox,
        LocationReadContext db)
    {
        _outbox = outbox;
        _db = db;
    }

    public async Task<Guid> Handle(CreateLocationCommand command)
    {
        var location = Location.Create(
            command.UserId,
            command.Coordinates,
            command.Display);

        await _db.SaveChangesWithRetryAsync(ct =>
            _outbox.AppendGeoEventsAsync(location.Id, location.Events, ct));
        return location.Id;
    }
}
