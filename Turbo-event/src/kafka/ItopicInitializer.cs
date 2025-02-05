namespace Turbo_event.kafka;

public interface ITopicInitializer
{
    Task EnsureTopicExists(string topic);
}