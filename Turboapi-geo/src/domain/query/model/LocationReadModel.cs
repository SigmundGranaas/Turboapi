
using NetTopologySuite.Geometries;

namespace Turboapi_geo.domain.query.model;

public class LocationReadEntity
{
    public required Guid Id { get; set; }
    public required string OwnerId { get; set; }
    public required Point Geometry { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; }
    public string Icon { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}