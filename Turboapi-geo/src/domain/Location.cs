using Medo;
using NetTopologySuite.Geometries;
using Turboapi_geo.domain;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.value;

public class Location : AggregateRoot
    {
            public Guid Id { get; private set; }
            public string OwnerId { get; private set; }
            public Point Geometry { get; private set; }
            public DisplayInformation DisplayInformation { get; private set; }

            private Location() { }

            public static Location From(Guid id, string ownerId, Point geometry, DisplayInformation displayInformation)
            {
                var location = new Location
                {
                    Id = Uuid7.FromGuid(id),
                    OwnerId = ownerId,
                    Geometry = geometry,
                    DisplayInformation = displayInformation
                };
                return location;
            }
            
            public static Location From(Guid id, string ownerId, Point geometry)
            {
                var location = new Location
                {
                    Id = Uuid7.FromGuid(id),
                    OwnerId = ownerId,
                    Geometry = geometry,
                    DisplayInformation = DisplayInformation.CreateDefault()
                };
                return location;
            }
            
            public static Location Create(Guid id, string ownerId, Point geometry)
            {
                var location = new Location
                {
                    Id = Uuid7.FromGuid(id),
                    OwnerId = ownerId,
                    Geometry = geometry,
                    DisplayInformation = DisplayInformation.CreateDefault()
                };
                
                location.AddEvent(new LocationCreated(location.Id, ownerId, geometry, location.DisplayInformation));
                return location;
            }
            
            public static Location Create(string ownerId, Point geometry)
            {
                var location = new Location
                {
                    Id = Uuid7.NewUuid7(),
                    OwnerId = ownerId,
                    Geometry = geometry,
                    DisplayInformation = DisplayInformation.CreateDefault()
                };

                location.AddEvent(new LocationCreated(location.Id, ownerId, geometry, location.DisplayInformation));
                return location;
            }
            
                     
            public static Location Create(Guid ownerId, Point geometry)
            {
                var location = new Location
                {
                    Id = Uuid7.NewUuid7(),
                    OwnerId = ownerId.ToString(),
                    Geometry = geometry,
                    DisplayInformation = DisplayInformation.CreateDefault()
                };

                location.AddEvent(new LocationCreated(location.Id, ownerId.ToString(), geometry, location.DisplayInformation));
                return location;
            }
            
            public static Location Create(string ownerId, Point geometry, DisplayInformation displayInformation)
            {
                var location = new Location
                {
                    Id = Uuid7.NewUuid7(),
                    OwnerId = ownerId,
                    Geometry = geometry,
                    DisplayInformation = displayInformation
                };

                location.AddEvent(new LocationCreated(location.Id, ownerId, geometry, location.DisplayInformation));
                return location;
            }
            
            public static Location Create(Guid id, string ownerId, Point geometry, DisplayInformation displayInformation)
            {
                var location = new Location
                {
                    Id = id,
                    OwnerId = ownerId,
                    Geometry = geometry,
                    DisplayInformation = displayInformation
                };

                location.AddEvent(new LocationCreated(location.Id, ownerId, geometry, displayInformation));
                return location;
            }

            public void UpdatePosition(Point newGeometry)
            {
                Geometry = newGeometry;
                AddEvent(new LocationPositionChanged(Id, newGeometry));
            }

            public void UpdateDisplayInformation(DisplayInformation displayInformation)
            {
                DisplayInformation = displayInformation;
                AddEvent(new LocationDisplayInformationChanged(Id, displayInformation.Name, displayInformation.Description, displayInformation.Icon));
            }

            public void Delete()
            {
                AddEvent(new LocationDeleted(Id, OwnerId));
            }
    }