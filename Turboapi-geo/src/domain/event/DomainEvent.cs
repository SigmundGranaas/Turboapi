using System.Text.Json.Serialization;
using Medo;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.domain.events
{
    /// <summary>
    /// Base event properties
    /// </summary>
    public abstract record DomainEvent
    {
        [JsonPropertyName("id")]
        public Guid Id { get; init; } = Uuid7.NewUuid7();

        [JsonPropertyName("occurredAt")]
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        // EventType is derived. If you want to ensure its serialized name and presence:
        [JsonPropertyName("eventType")]
        public string EventType => GetType().Name;
    }

    /// <summary>
    /// Location created event
    /// </summary>
    public record LocationCreated : DomainEvent
    {
        [JsonPropertyName("locationId")]
        public Guid LocationId { get; init; }

        [JsonPropertyName("ownerId")]
        public Guid OwnerId { get; init; }

        [JsonPropertyName("coordinates")]
        public Coordinates Coordinates { get; init; }

        [JsonPropertyName("display")]
        public DisplayInformation Display { get; init; }

        [JsonConstructor]
        public LocationCreated(
            Guid locationId,
            Guid ownerId,
            Coordinates coordinates,
            DisplayInformation display)
        {
            LocationId = locationId;
            OwnerId = ownerId;
            Coordinates = coordinates;
            Display = display;
        }
    }

    /// <summary>
    /// Location updated event with changes
    /// </summary>
    public record LocationUpdated : DomainEvent
    {
        [JsonPropertyName("locationId")]
        public Guid LocationId { get; init; }

        [JsonPropertyName("ownerId")]
        public Guid OwnerId { get; init; }

        [JsonPropertyName("updates")]
        public LocationUpdateParameters Updates { get; init; }

        [JsonConstructor]
        public LocationUpdated(
            Guid locationId,
            Guid ownerId,
            LocationUpdateParameters updates)
        {
            LocationId = locationId;
            OwnerId = ownerId;
            Updates = updates;
        }
    }

    /// <summary>
    /// Location deleted event
    /// </summary>
    public record LocationDeleted : DomainEvent
    {
        [JsonPropertyName("locationId")]
        public Guid LocationId { get; init; }

        [JsonPropertyName("ownerId")]
        public Guid OwnerId { get; init; }

        [JsonConstructor]
        public LocationDeleted(Guid locationId, Guid ownerId)
        {
            LocationId = locationId;
            OwnerId = ownerId;
        }
    }
}