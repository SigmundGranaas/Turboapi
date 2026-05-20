using Turboapi.Activities.Hiking.value;

namespace Turboapi.Activities.Hiking.domain.handler;

public sealed record CreateHikingActivityCommand(
    Guid CallerId, string Name, string? Description, string RouteWkt, HikingDetails Details);

public sealed record UpdateHikingActivityCommand(
    Guid CallerId, Guid ActivityId, string? Name, string? Description, string? RouteWkt, HikingDetails? Details);

public sealed record DeleteHikingActivityCommand(Guid CallerId, Guid ActivityId);
