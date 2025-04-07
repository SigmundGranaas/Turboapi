using GeoSpatial.Domain.Events;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.exception;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.value;

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

        if (location.OwnerId != command.OwnerId.ToString())
        {
            throw new UnauthorizedException("Only the owner is allowed to update the location");
        }

        if (command.LocationData != null)
        {
            var newPoint = _geometryFactory.CreatePoint(
                new Coordinate(command.LocationData.Longitude, command.LocationData.Latitude)
            );

            location.UpdatePosition(newPoint);
        }

        if (command.Name != null || command.Description != null || command.Icon != null)
        {
            var name = command.Name ?? location.DisplayInformation.Name;
            var description = command.Description ?? location.DisplayInformation.Description;
            var icon = command.Icon ?? location.DisplayInformation.Icon;
            location.UpdateDisplayInformation(DisplayInformation.of(name, description, icon));
        }   
        
        await _eventStore.AppendEvents(location.Events);
    }
}