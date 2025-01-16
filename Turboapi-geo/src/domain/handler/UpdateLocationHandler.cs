using GeoSpatial.Domain.Events;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;

namespace Turboapi_geo.domain.handler;

public class UpdateLocationPositionHandler
{
    private readonly ILocationReadModelRepository _repository;
    private readonly IEventWriter _eventStore;
    private readonly GeometryFactory _geometryFactory;

    public UpdateLocationPositionHandler(
        ILocationReadModelRepository repository,
        IEventWriter eventStore,
        GeometryFactory geometryFactory)
    {
        _repository = repository;
        _eventStore = eventStore;
        _geometryFactory = geometryFactory;
    }

    public async Task Handle(Commands.UpdateLocationPositionCommand command)
    {
        var location = await _repository.GetById(command.LocationId);
        if (location == null)
            throw new LocationNotFoundException(command.LocationId.ToString());

        var newPoint = _geometryFactory.CreatePoint(
            new Coordinate(command.Longitude, command.Latitude)
        );

        location.UpdatePosition(newPoint);
        await _eventStore.AppendEvents(location.Events);
    }
}