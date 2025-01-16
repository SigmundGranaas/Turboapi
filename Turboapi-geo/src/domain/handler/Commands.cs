namespace Turboapi_geo.domain.handler;

public class Commands
{
    public record CreateLocationCommand(string OwnerId, double Longitude, double Latitude);
    public record UpdateLocationPositionCommand(Guid LocationId, double Longitude, double Latitude);
    public record DeleteLocationCommand(Guid LocationId);
}