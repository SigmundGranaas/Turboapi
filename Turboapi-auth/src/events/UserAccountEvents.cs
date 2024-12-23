namespace Turboapi.events;

public abstract class UserAccountEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public Guid AccountId { get; set; }
    public string Provider { get; set; }
}

public class UserCreatedEvent : UserAccountEvent
{
    public string Email { get; set; }
    public Dictionary<string, string> AdditionalInfo { get; set; }
}

public class UserLoginEvent : UserAccountEvent
{
    public DateTime LoginTimestamp { get; set; }
}