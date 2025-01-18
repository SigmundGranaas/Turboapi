using GeoSpatial.Domain.Events;
using NetTopologySuite.Geometries;

namespace Turboapi_geo.domain.handler;

public class CreateLocationHandler
{
    private readonly IEventWriter _eventStore;
    private readonly GeometryFactory _geometryFactory;

    public CreateLocationHandler(
        IEventWriter eventStore,
        GeometryFactory geometryFactory)
    {
        _eventStore = eventStore;
        _geometryFactory = geometryFactory;
    }

    public async Task<Guid> Handle(Commands.CreateLocationCommand command)
    {
        var point = _geometryFactory.CreatePoint(
            new Coordinate(command.Longitude, command.Latitude)
        );

        var location = Location.Create(command.OwnerId.ToString(), point);
        await _eventStore.AppendEvents(location.Events);

        return location.Id;
    }
}