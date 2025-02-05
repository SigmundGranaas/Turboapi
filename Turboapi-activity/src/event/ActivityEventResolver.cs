using Turboauth_activity.domain.events;

public class ActivityEventTopicResolver : IEventTopicResolver
{
    private readonly Dictionary<Type, string> _topicMappings;

    public ActivityEventTopicResolver()
    {
        _topicMappings = new Dictionary<Type, string>
        {
            { typeof(ActivityCreated), "activities" },
            { typeof(ActivityUpdated), "activities" },
            { typeof(ActivityDeleted), "activities" },
            { typeof(ActivityPositionCreated), "positions" }
        };
    }

    public string ResolveTopicFor(Event @event)
    {
        if (!_topicMappings.TryGetValue(@event.GetType(), out var topic))
        {
            throw new ArgumentException($"No topic mapping found for event type: {@event.GetType().Name}");
        }
        return topic;
    }
}