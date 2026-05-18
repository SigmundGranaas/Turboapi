using Turbo.Messaging;
using Turboauth_activity.domain;

namespace Turboauth_activity.domain.events;

public record ActivityCreated(
    Guid activity,
    Guid OwnerId,
    Guid position,
    string name,
    string description,
    string icon
) : DomainEvent;

public record ActivityUpdated(
    Guid ActivityId,
    string name,
    string description,
    string icon
) : DomainEvent;

public record ActivityPositionCreated(
    Guid positionId,
    Position position,
    Guid activityId,
    Guid ownerId
) : DomainEvent;

public record ActivityDeleted(
    Guid activityId
) : DomainEvent;
