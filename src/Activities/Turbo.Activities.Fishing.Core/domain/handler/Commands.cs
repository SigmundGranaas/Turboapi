using Turboapi.Activities.Fishing.value;

namespace Turboapi.Activities.Fishing.domain.handler;

public sealed record CreateFishingActivityCommand(
    Guid CallerId,
    string Name,
    string? Description,
    double Longitude,
    double Latitude,
    FishingDetails Details);

public sealed record UpdateFishingActivityCommand(
    Guid CallerId,
    Guid ActivityId,
    string? Name,
    string? Description,
    double? Longitude,
    double? Latitude,
    FishingDetails? Details);

public sealed record DeleteFishingActivityCommand(
    Guid CallerId,
    Guid ActivityId);
