using Turboauth_activity.domain.command;
using Turboauth_activity.domain.exception;
using Turboauth_activity.domain.query;

namespace Turboauth_activity.domain.handler;

public class EditActivityHandler
{
    private readonly IEventStoreWriter _eventStore;
    private readonly IActivityReadRepository _repo;


    public EditActivityHandler(
        IEventStoreWriter eventStore,
        IActivityReadRepository repo)
    {
        _eventStore = eventStore;
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
        
        await _eventStore.AppendEvents(activity.Events);
        
        return ActivityQueryDto.FromActivity(activity);
    }
}