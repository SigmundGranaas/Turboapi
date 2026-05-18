using Turbo.Outbox;
using Turboauth_activity.domain.command;

namespace Turboauth_activity.domain.handler;

public class CreateActivityHandler
{

    private readonly IOutbox<ActivityScope> _outbox;
    private readonly IUnitOfWork<ActivityScope> _uow;

    public CreateActivityHandler(IOutbox<ActivityScope> outbox, IUnitOfWork<ActivityScope> uow)
    {
        _outbox = outbox;
        _uow = uow;
    }

    public async Task<Guid> Handle(CreateActivityCommand command)
    {
        var activity = Activity.Create(
            command.OwnerId, command.Position, command.Name, command.Description, command.Icon);

        await _uow.SaveChangesAsync(ct =>
            _outbox.AppendEventsAsync(activity.Id, activity.Events, ct));

        return activity.Id;
    }
}
