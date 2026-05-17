using Turbo.Messaging;
using Turbo.Outbox;

namespace Turboauth_activity.infrastructure;

internal static class ActivityOutboxExtensions
{
    private const string Source = "activity";

    public static async Task AppendActivityEventsAsync(
        this IOutbox<Turboauth_activity.data.ActivityContext> outbox,
        Guid aggregateId,
        IEnumerable<Event> events,
        CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string> { ["aggregateId"] = aggregateId.ToString() };
        foreach (var @event in events)
        {
            var envelope = EventEnvelopeFactory.For(@event, Source, headers);
            await outbox.AppendAsync(envelope, cancellationToken);
        }
    }
}
