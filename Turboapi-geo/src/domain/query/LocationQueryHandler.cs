using Turboapi_geo.domain.queries;

namespace Turboapi_geo.domain.query;

public class GetLocationByIdHandler
{
    private readonly ILocationReadModelRepository _readModel;

    public GetLocationByIdHandler(ILocationReadModelRepository readModel)
    {
        _readModel = readModel;
    }

    public async Task<LocationDto?> Handle(GetLocationByIdQuery query)
    {
        var locationRead = await _readModel.GetById(query.LocationId);
        if (locationRead == null)
        {
            return null;
        }
        else
        {
            return  new LocationDto(locationRead.Id, Guid.Parse(locationRead.OwnerId), locationRead.Geometry);

        }

    }
}

public class GetLocationsInExtentHandler
{
    private readonly ILocationReadModelRepository _readModel;

    public GetLocationsInExtentHandler(ILocationReadModelRepository readModel)
    {
        _readModel = readModel;
    }

    public async Task<IEnumerable<LocationDto>> Handle(GetLocationsInExtentQuery query)
    {
        var locations = await _readModel.GetLocationsInExtent(
            query.OwnerId,
            query.MinLongitude,
            query.MinLatitude,
            query.MaxLongitude,
            query.MaxLatitude
        );
        return locations.Select(loc => new LocationDto(loc.Id, Guid.Parse(loc.OwnerId), loc.Geometry));
    }
}