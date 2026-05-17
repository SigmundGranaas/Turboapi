using Turbo.Outbox;
using Turboauth_activity.data;
using Turboauth_activity.domain.command;
using Turboauth_activity.domain.exception;
using Turboauth_activity.domain.query;
using Turboauth_activity.infrastructure;

namespace Turboauth_activity.domain.handler;

public class EditActivityHandler
{
    private readonly IOutbox _outbox;
    private readonly ActivityContext _db;
    private readonly IActivityReadRepository _repo;

    public EditActivityHandler(
        IOutbox outbox,
        ActivityContext db,
        IActivityReadRepository repo)
    {
        _outbox = outbox;
        _db = db;
        _repo = repo;
    }

    public async Task<ActivityQueryDto> Handle(EditActivityCommand command)
    {
        var activity = await _repo.GetById(command.ActivityID);
        if (activity == null)
        {
            throw new ActivityNotFoundException($"Activity with id {command.ActivityID} not found");
        }

        activity.Update(command.UserID, command.Name, command.Description, command.Icon);

        await _outbox.AppendActivityEventsAsync(activity.Id, activity.Events);
        await _db.SaveChangesAsync();

        return ActivityQueryDto.FromActivity(activity);
    }
}
