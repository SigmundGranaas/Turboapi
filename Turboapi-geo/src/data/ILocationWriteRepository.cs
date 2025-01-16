namespace Turboapi_geo.domain.query.model;

public interface ILocationWriteRepository
{
    Task<LocationReadEntity?> GetById(Guid id);
    Task Add(LocationReadEntity entity);
    Task Update(LocationReadEntity entity);
    Task Delete(LocationReadEntity entity);
}