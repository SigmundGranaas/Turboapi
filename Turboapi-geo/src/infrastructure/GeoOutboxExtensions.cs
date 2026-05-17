using Turbo.Messaging;
using Turbo.Outbox;
using Turboapi_geo.domain.events;

namespace Turboapi_geo.infrastructure;

internal static class GeoOutboxExtensions
{
    private const string Source = "geo";

    public static async Task AppendGeoEventsAsync(
        this IOutbox<Turboapi_geo.domain.query.model.LocationReadContext> outbox,
        Guid aggregateId,
        IEnumerable<DomainEvent> events,
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
