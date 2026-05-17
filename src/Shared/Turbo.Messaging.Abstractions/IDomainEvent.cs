namespace Turbo.Messaging;

/// <summary>
/// Root marker interface for every domain event produced by a module.
/// Transport metadata (id, timestamp, headers, content type) lives on
/// <see cref="EventEnvelope"/> rather than on the event itself, so concrete
/// events stay free of broker concerns.
/// </summary>
public interface IDomainEvent
{
}
