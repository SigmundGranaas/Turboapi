
using NetTopologySuite.Geometries;

namespace Turboapi_geo.domain.query.model;

public interface ILocationWriteRepository
{
    Task<LocationReadEntity?> GetById(Guid id);
    Task Add(LocationReadEntity entity);
    Task Update(LocationReadEntity entity);
    Task Delete(LocationReadEntity entity);
    
    // Partial updates can help in situations where we're considering performing reads to increment fields
    // This avoids race conditions in cases where we're reading the entire entity and updating some fields.
    Task UpdatePosition(Guid id, Point geometry);
    Task UpdateDisplayInformation(Guid id, string name, string? description, string? icon);
}