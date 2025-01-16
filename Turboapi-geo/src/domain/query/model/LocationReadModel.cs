
using NetTopologySuite.Geometries;

namespace Turboapi_geo.domain.query.model;

public class LocationReadEntity
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; }
    public Point Geometry { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}