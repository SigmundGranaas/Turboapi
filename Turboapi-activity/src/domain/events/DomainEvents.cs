
namespace Turboauth_activity.domain.events;

public record ActivityCreated(
    Guid activity,
    Guid OwnerId,
    Guid position,
    String name,
    String description,
    String icon
) : Event;

public record ActivityUpdated(
    Guid ActivityId,
    String name,
    String description,
    String icon
) : Event;


public record ActivityPositionCreated(
    Guid positionId,
    Position position,
    Guid activityId,
    Guid ownerId
) : Event;

public record ActivityDeleted(
    Guid activityId
) : Event;