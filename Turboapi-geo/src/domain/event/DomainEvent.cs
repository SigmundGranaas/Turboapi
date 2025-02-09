using System.Text.Json.Serialization;
using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.domain.events;

[JsonDerivedType(typeof(LocationCreated), typeDiscriminator: nameof(LocationCreated))]
[JsonDerivedType(typeof(LocationPositionChanged), typeDiscriminator: nameof(LocationPositionChanged))]
[JsonDerivedType(typeof(LocationDeleted), typeDiscriminator: nameof(LocationDeleted))]
[JsonDerivedType(typeof(CreatePositionEvent), typeDiscriminator: nameof(CreatePositionEvent))]

public abstract record DomainEvent
{
    public Guid Id { get; } = Uuid7.NewUuid7();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public record LocationCreated(
    Guid LocationId,
    string OwnerId,
    Point Geometry
) : DomainEvent;

public record LocationPositionChanged(
    Guid LocationId,
    Point Geometry
) : DomainEvent;

public record LocationDeleted(
    Guid LocationId,
    string OwnerId
) : DomainEvent;


public record CreatePositionEvent(
    Guid positionId,
    LatLng position,
    Guid activityId,
    Guid ownerId
) : DomainEvent;