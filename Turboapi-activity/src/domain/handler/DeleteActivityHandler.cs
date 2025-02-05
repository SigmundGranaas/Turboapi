using Turboauth_activity.domain.command;
using Turboauth_activity.domain.exception;
using Turboauth_activity.domain.query;

namespace Turboauth_activity.domain.handler;

public class DeleteActivityHandler
{
    private readonly IEventStoreWriter _eventStore;
    private readonly IActivityReadRepository _repo;


    public DeleteActivityHandler(
        IEventStoreWriter eventStore,
        IActivityReadRepository repo)
    {
        _eventStore = eventStore;
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
        
        await _eventStore.AppendEvents(activity.Events);
        
        return activity.Id;
    }
}