using Turbo.Messaging;

public abstract record Event : IDomainEvent
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime Timestamp { get; protected set; } = DateTime.UtcNow;
}