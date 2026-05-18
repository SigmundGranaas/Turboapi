using Turbo.Outbox;
using Turboapi_geo.domain.commands;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;

namespace Turboapi_geo.domain.handler;

public class UpdateLocationHandler
{
    private const string Source = "geo";

    private readonly ILocationReadRepository _repository;
    private readonly IOutbox<IGeoScope> _outbox;
    private readonly IUnitOfWork<IGeoScope> _uow;

    public UpdateLocationHandler(
        ILocationReadRepository repository,
        IOutbox<IGeoScope> outbox,
        IUnitOfWork<IGeoScope> uow)
    {
        _repository = repository;
        _outbox = outbox;
        _uow = uow;
    }

    public async Task<Turboapi_geo.domain.model.Location> Handle(UpdateLocationCommand command)
    {
        var location = await _repository.GetById(command.LocationId);
        if (location == null)
            throw new LocationNotFoundException(command.LocationId.ToString());

        location.Update(command.UserId, command.Updates);

        await _uow.SaveChangesAsync(ct =>
            _outbox.AppendEventsAsync(location.Id, Source, location.Events, ct));
        return location;
    }
}
