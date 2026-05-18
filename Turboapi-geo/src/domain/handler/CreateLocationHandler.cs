using Turbo.Outbox;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.model;

namespace Turboapi_geo.domain.handler;

public class CreateLocationHandler
{
    private const string Source = "geo";

    private readonly IOutbox<IGeoScope> _outbox;
    private readonly IUnitOfWork<IGeoScope> _uow;

    public CreateLocationHandler(IOutbox<IGeoScope> outbox, IUnitOfWork<IGeoScope> uow)
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
            _outbox.AppendEventsAsync(location.Id, Source, location.Events, ct));
        return location.Id;
    }
}
