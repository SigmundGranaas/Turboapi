using Turbo.Outbox;
using Turboauth_activity.domain.command;
using Turboauth_activity.domain.exception;
using Turboauth_activity.domain.query;

namespace Turboauth_activity.domain.handler;

public class DeleteActivityHandler
{

    private readonly IOutbox<ActivityScope> _outbox;
    private readonly IUnitOfWork<ActivityScope> _uow;
    private readonly IActivityReadRepository _repo;

    public DeleteActivityHandler(
        IOutbox<ActivityScope> outbox,
        IUnitOfWork<ActivityScope> uow,
        IActivityReadRepository repo)
    {
        _outbox = outbox;
        _uow = uow;
        _repo = repo;
    }

    public async Task<Guid> Handle(DeleteActivityCommand command)
    {
        var activity = await _repo.GetById(command.ActivityID);
        if (activity == null)
        {
            throw new ActivityNotFoundException($"Activity with id {command.ActivityID} not found");
        }

        activity.Delete(command.UserID);

        await _uow.SaveChangesAsync(ct =>
            _outbox.AppendEventsAsync(activity.Id, activity.Events, ct));

        return activity.Id;
    }
}
