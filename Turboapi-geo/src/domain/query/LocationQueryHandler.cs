using Turboapi_geo.domain.queries;

namespace Turboapi_geo.domain.query;

public class GetLocationByIdHandler
{
    private readonly ILocationReadRepository _read;

    public GetLocationByIdHandler(ILocationReadRepository read)
    {
        _read = read;
    }

    public async Task<LocationData?> Handle(GetLocationByIdQuery query)
    {
        var locationRead = await _read.GetById(query.LocationId);
        if (locationRead == null)
        {
            return null;
        }
        
        return  new LocationData(locationRead.Id, locationRead.OwnerId, locationRead.Coordinates, locationRead.Display);
    }
}

public class GetLocationsInExtentHandler
{
    private readonly ILocationReadRepository _read;

    public GetLocationsInExtentHandler(ILocationReadRepository read)
    {
        _read = read;
    }

    public async Task<IEnumerable<LocationData>> Handle(GetLocationsInExtentQuery query)
    {
        var locations = await _read.GetLocationsInExtent(
            query.Owner,
            query.MinLongitude,
            query.MinLatitude,
            query.MaxLongitude,
            query.MaxLatitude
        );
        
        return locations.Select(loc => new LocationData(loc.Id, loc.OwnerId, loc.Coordinates, loc.Display));
    }
}