using Turboapi_geo.domain.model;

namespace Turboapi_geo.domain.query;

public interface ILocationReadRepository
{
    Task<Location?> GetById(Guid id);
    Task<IEnumerable<Location>> GetLocationsInExtent(
        Guid ownerId,
        double minLongitude,
        double minLatitude,
        double maxLongitude,
        double maxLatitude
    );
}