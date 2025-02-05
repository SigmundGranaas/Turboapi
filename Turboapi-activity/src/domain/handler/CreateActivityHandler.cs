using Turboauth_activity.domain.command;

namespace Turboauth_activity.domain.handler;

public class CreateActivityHandler
{
    private readonly IEventStoreWriter _eventStore;

    public CreateActivityHandler(
        IEventStoreWriter eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<Guid> Handle(CreateActivityCommand command)
    {
        var activity = Activity.Create(command.OwnerId, command.Position, command.Name, command.Description,
            command.Icon);
        
        await _eventStore.AppendEvents(activity.Events);

        return activity.Id;
    }
}