
using Turboauth_activity.domain;
using Turboauth_activity.domain.query;

public class InMemoryActivityReadModel : IActivityReadRepository
{
    private readonly Dictionary<Guid, Activity> _activities;

    public InMemoryActivityReadModel(Dictionary<Guid, Activity> activities)
    {
        _activities = activities;
    }

    public Task<Activity?> GetById(Guid id)
    {
        _activities.TryGetValue(id, out var activity);
        if (activity == null)
        {
            return Task.FromResult<Activity?>(null);
        }
        
        return Task.FromResult<Activity?>(Activity.From(activity.Id, activity.OwnerId, activity.Position, activity.Name, activity.Description, activity.Icon));
    }
}