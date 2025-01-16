
using NetTopologySuite.Geometries;

namespace Turboapi_geo.domain.queries
{
    public record GetLocationByIdQuery(Guid LocationId);
    
    public record GetLocationsInExtentQuery(
        string OwnerId,
        double MinLongitude,
        double MinLatitude,
        double MaxLongitude,
        double MaxLatitude
        ); 
    
    public record LocationDto(Guid id, Guid ownerId, Point geometry);
}


