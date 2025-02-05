
public abstract record Event
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime Timestamp { get; protected set; } = DateTime.UtcNow;
}