namespace Turboapi_geo.domain.handler;

public class Commands
{
    public record CreateLocationCommand(Guid OwnerId, double Longitude, double Latitude);
    public record CreatePredefinedLocationCommand(Guid id, Guid OwnerId, double Longitude, double Latitude);
    public record UpdateLocationPositionCommand(Guid OwnerId, Guid LocationId, double Longitude, double Latitude);
    public record DeleteLocationCommand(Guid LocationId, Guid OwnerId);
}