using Turbo.Outbox;
using Turboauth_activity.data;
using Turboauth_activity.domain.command;
using Turboauth_activity.infrastructure;

namespace Turboauth_activity.domain.handler;

public class CreateActivityHandler
{
    private readonly IOutbox _outbox;
    private readonly ActivityContext _db;

    public CreateActivityHandler(IOutbox outbox, ActivityContext db)
    {
        _outbox = outbox;
        _db = db;
    }

    public async Task<Guid> Handle(CreateActivityCommand command)
    {
        var activity = Activity.Create(
            command.OwnerId, command.Position, command.Name, command.Description, command.Icon);

        await _outbox.AppendActivityEventsAsync(activity.Id, activity.Events);
        await _db.SaveChangesAsync();

        return activity.Id;
    }
}
