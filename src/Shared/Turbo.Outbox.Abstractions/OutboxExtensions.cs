using Turbo.Messaging;

namespace Turbo.Outbox;

public static class OutboxExtensions
{
    /// <summary>
    /// Convenience helper: turns a sequence of <see cref="IDomainEvent"/>
    /// instances into <see cref="EventEnvelope"/>s tagged with the given
    /// module <paramref name="source"/> and aggregate id, then appends them
    /// to <paramref name="outbox"/>. Handlers call this rather than
    /// building envelopes by hand, which would otherwise drag
    /// <see cref="EventEnvelopeFactory"/> calls into every module's
    /// application layer.
    /// </summary>
    public static async Task AppendEventsAsync<TScope, TEvent>(
        this IOutbox<TScope> outbox,
        Guid aggregateId,
        string source,
        IEnumerable<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        var headers = new Dictionary<string, string> { ["aggregateId"] = aggregateId.ToString() };
        foreach (var @event in events)
        {
            var envelope = EventEnvelopeFactory.For(@event, source, headers);
            await outbox.AppendAsync(envelope, cancellationToken);
        }
    }
}
