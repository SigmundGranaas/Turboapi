namespace Turboapi_geo.domain.query;

public interface ILocationReadModelRepository
{
    Task<Location?> GetById(Guid id);
    Task<IEnumerable<Location>> GetLocationsInExtent(
        string ownerId,
        double minLongitude,
        double minLatitude,
        double maxLongitude,
        double maxLatitude
    );
}