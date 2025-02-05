using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain;
using Turboapi_geo.domain.events;

    public class Location : AggregateRoot
    {
            public Guid Id { get; private set; }
            public string OwnerId { get; private set; }
            public Point Geometry { get; private set; }
            public bool IsDeleted { get; private set; }

            private Location() { }

            public static Location From(Guid id, string ownerId, Point geometry)
            {
                var location = new Location
                {
                    Id = Uuid7.FromGuid(id),
                    OwnerId = ownerId,
                    Geometry = geometry,
                    IsDeleted = false
                };
                return location;
            }
            
            public static Location Create(string ownerId, Point geometry)
            {
                var location = new Location
                {
                    Id = Uuid7.NewUuid7(),
                    OwnerId = ownerId,
                    Geometry = geometry,
                    IsDeleted = false
                };

                location.AddEvent(new LocationCreated(location.Id, ownerId, geometry));
                return location;
            }

            public void UpdatePosition(Point newGeometry)
            {
                if (IsDeleted)
                    throw new InvalidOperationException("Cannot update a deleted location");

                Geometry = newGeometry;
                AddEvent(new LocationPositionChanged(Id, newGeometry));
            }

            public void Delete()
            {
                if (IsDeleted)
                    return;

                IsDeleted = true;
                AddEvent(new LocationDeleted(Id, OwnerId));
            }
    }
