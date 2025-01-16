using System.Text.Json.Serialization;
using Medo;
using NetTopologySuite.Geometries;

namespace Turboapi_geo.domain.events;

[JsonDerivedType(typeof(LocationCreated), typeDiscriminator: nameof(LocationCreated))]
[JsonDerivedType(typeof(LocationPositionChanged), typeDiscriminator: nameof(LocationPositionChanged))]
[JsonDerivedType(typeof(LocationDeleted), typeDiscriminator: nameof(LocationDeleted))]
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