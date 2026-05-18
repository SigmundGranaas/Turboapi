using Turbo.Outbox;
using Turboauth_activity.domain.command;

namespace Turboauth_activity.domain.handler;

public class CreateActivityHandler
{
    private const string Source = "activity";

    private readonly IOutbox<IActivityScope> _outbox;
    private readonly IUnitOfWork<IActivityScope> _uow;

    public CreateActivityHandler(IOutbox<IActivityScope> outbox, IUnitOfWork<IActivityScope> uow)
    {
        _outbox = outbox;
        _uow = uow;
    }

    public async Task<Guid> Handle(CreateActivityCommand command)
    {
        var activity = Activity.Create(
            command.OwnerId, command.Position, command.Name, command.Description, command.Icon);

        await _uow.SaveChangesAsync(ct =>
            _outbox.AppendEventsAsync(activity.Id, Source, activity.Events, ct));

        return activity.Id;
    }
}
