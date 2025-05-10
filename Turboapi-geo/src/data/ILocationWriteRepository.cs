using Turboapi_geo.data.model;
using Turboapi_geo.domain.value;

namespace Turboapi_geo.domain.query.model;

public interface ILocationWriteRepository
{
    Task<LocationEntity?> GetById(Guid id);
    Task Add(LocationEntity entity);
    Task Update(LocationEntity entity);
    Task Delete(LocationEntity entity);
    Task UpdatePartial(Guid id, Coordinates? coordinates, DisplayUpdate? display);
}