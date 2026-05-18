using Turbo.Outbox;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.model;

namespace Turboapi_geo.domain.handler;

public class CreateLocationHandler
{

    private readonly IOutbox<GeoScope> _outbox;
    private readonly IUnitOfWork<GeoScope> _uow;

    public CreateLocationHandler(IOutbox<GeoScope> outbox, IUnitOfWork<GeoScope> uow)
    {
        _outbox = outbox;
        _uow = uow;
    }

    public async Task<Guid> Handle(CreateLocationCommand command)
    {
        var location = Location.Create(
            command.UserId,
            command.Coordinates,
            command.Display);

        await _uow.SaveChangesAsync(ct =>
            _outbox.AppendEventsAsync(location.Id, location.Events, ct));
        return location.Id;
    }
}
