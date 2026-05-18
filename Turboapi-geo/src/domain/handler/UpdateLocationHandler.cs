using Turbo.Outbox;
using Turbo.Outbox.Postgres;
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

    public UpdateLocationHandler(
        ILocationReadRepository repository,
        IOutbox<LocationReadContext> outbox,
        LocationReadContext db)
    {
        _repository = repository;
        _outbox = outbox;
        _db = db;
    }

    public async Task<Turboapi_geo.domain.model.Location> Handle(UpdateLocationCommand command)
    {
        var location = await _repository.GetById(command.LocationId);
        if (location == null)
            throw new LocationNotFoundException(command.LocationId.ToString());

        location.Update(command.UserId, command.Updates);

        await _db.SaveChangesWithRetryAsync(ct =>
            _outbox.AppendGeoEventsAsync(location.Id, location.Events, ct));
        return location;
    }
}
