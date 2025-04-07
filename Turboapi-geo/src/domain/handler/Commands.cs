using Turboapi_geo.controller;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.domain.handler;

public class Commands
{
    public record CreateLocationCommand(Guid OwnerId, double Longitude, double Latitude, DisplayInformation DisplayInformation);
    public record CreatePredefinedLocationCommand(Guid id, Guid OwnerId, double Longitude, double Latitude);
    public record UpdateLocationPositionCommand(Guid OwnerId, Guid LocationId, LocationData? LocationData = null, string? Name = null, string? Description = null, string? Icon = null);
    public record DeleteLocationCommand(Guid LocationId, Guid OwnerId);
}