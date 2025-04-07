
using NetTopologySuite.Geometries;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.domain.queries
{
    public record GetLocationByIdQuery(Guid LocationId, Guid Owner);
    
    public record GetLocationsInExtentQuery(
        Guid Owner,
        double MinLongitude,
        double MinLatitude,
        double MaxLongitude,
        double MaxLatitude
        ); 
    
    public record LocationDto(Guid id, Guid ownerId, Point geometry, DisplayInformation displayInformation);
}


