namespace Turboapi_geo.test.integration;

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Testcontainers.PostgreSql;
using Xunit;

public class SpatialMappingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;
    private string _connectionString;
    private readonly GeometryFactory _geometryFactory;

    public SpatialMappingTests()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:17-master")
            .WithDatabase("geo")
            .WithUsername("postgres")
            .WithPassword("your_password")
            .Build();
        
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        _connectionString = _dbContainer.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
    }

    // Extended model to include areas and routes
    public class GeoLocation
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public Geometry Geometry { get; private set; }  // Changed to Geometry to support different types

        private GeoLocation() { }

        public static GeoLocation CreatePoint(string name, double longitude, double latitude)
        {
            var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
            return new GeoLocation
            {
                Id = Guid.NewGuid(),
                Name = name,
                Geometry = geometryFactory.CreatePoint(new Coordinate(longitude, latitude))
            };
        }

        public static GeoLocation CreatePolygon(string name, Coordinate[] coordinates)
        {
            var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
            // Ensure the polygon is closed (first and last points match)
            if (!coordinates[0].Equals2D(coordinates[^1]))
            {
                coordinates = coordinates.Concat(new[] { coordinates[0] }).ToArray();
            }
            return new GeoLocation
            {
                Id = Guid.NewGuid(),
                Name = name,
                Geometry = geometryFactory.CreatePolygon(coordinates)
            };
        }

        public static GeoLocation CreateLineString(string name, Coordinate[] coordinates)
        {
            var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
            return new GeoLocation
            {
                Id = Guid.NewGuid(),
                Name = name,
                Geometry = geometryFactory.CreateLineString(coordinates)
            };
        }
    }

    public class TestGeoContext : DbContext
    {
        public DbSet<GeoLocation> Locations { get; set; }

        public TestGeoContext(DbContextOptions options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GeoLocation>(entity =>
            {
                entity.ToTable("locations");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Geometry)
                    .HasColumnType("geometry(Geometry, 4326)");
                entity.HasIndex(e => e.Geometry)
                    .HasMethod("GIST");
            });
        }
    }

    private TestGeoContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestGeoContext>()
            .UseNpgsql(_connectionString, x => x.UseNetTopologySuite())
            .Options;

        return new TestGeoContext(options);
    }

    private record NearestLocation(
        Guid Id,
        string Name,
        Geometry Geometry,
        double DistanceMeters
    );

    [Fact]
    public async Task FindNearestLocations_ShouldReturnOrderedByDistance()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var oslo = GeoLocation.CreatePoint("Oslo", 10.757933, 59.911491);
        var bergen = GeoLocation.CreatePoint("Bergen", 5.324383, 60.397076);
        var trondheim = GeoLocation.CreatePoint("Trondheim", 10.396466, 63.430515);

        context.Locations.AddRange(oslo, bergen, trondheim);
        await context.SaveChangesAsync();

        // Point somewhere between Oslo and Bergen
        var searchPoint = _geometryFactory.CreatePoint(new Coordinate(8.0, 60.0));

        // Act - Find 2 nearest locations
        var nearest = await context.Database.SqlQuery<NearestLocation>(FormattableStringFactory.Create(@"
            SELECT 
                l.""Id"",
                l.""Name"",
                l.""Geometry"",
                ST_Distance(l.""Geometry""::geography, ST_SetSRID(ST_Point({0}, {1}), 4326)::geography) as ""DistanceMeters""
            FROM locations l
            ORDER BY l.""Geometry"" <-> ST_SetSRID(ST_Point({0}, {1}), 4326)
            LIMIT 2",
            searchPoint.X,
            searchPoint.Y
        )).ToListAsync();

        // Assert
        nearest.Should().HaveCount(2);
        nearest[0].Name.Should().Be("Bergen"); // Bergen should be closest
        nearest[1].Name.Should().Be("Oslo");   // Oslo should be second
    }

    [Fact]
    public async Task FindLocationsWithinPolygon_ShouldReturnContainedPoints()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Create a polygon representing roughly southern Norway
        var southernNorway = GeoLocation.CreatePolygon("Southern Norway", new[]
        {
            new Coordinate(4.0, 58.0),
            new Coordinate(4.0, 62.0),
            new Coordinate(12.0, 62.0),
            new Coordinate(12.0, 58.0),
            new Coordinate(4.0, 58.0)
        });

        var oslo = GeoLocation.CreatePoint("Oslo", 10.757933, 59.911491);
        var bergen = GeoLocation.CreatePoint("Bergen", 5.324383, 60.397076);
        var trondheim = GeoLocation.CreatePoint("Trondheim", 10.396466, 63.430515);

        context.Locations.AddRange(southernNorway, oslo, bergen, trondheim);
        await context.SaveChangesAsync();

        // Act
        var result = await context.Database.SqlQuery<GeoLocation>(FormattableStringFactory.Create(@"
            SELECT 
                l.""Id"",
                l.""Name"",
                l.""Geometry""
            FROM locations l
            WHERE ST_Within(l.""Geometry"", (
                SELECT ""Geometry"" 
                FROM locations 
                WHERE ""Name"" = 'Southern Norway'
            ))
            AND ST_GeometryType(l.""Geometry"") = 'ST_Point'",
            Array.Empty<object>()
        )).ToListAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(l => l.Name == "Oslo");
        result.Should().Contain(l => l.Name == "Bergen");
        result.Should().NotContain(l => l.Name == "Trondheim");
    }

    [Fact]
    public async Task FindLocationsAlongRoute_ShouldReturnPointsWithinBuffer()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Create a route (e.g., a highway)
        var route = GeoLocation.CreateLineString("E18 Highway", new[]
        {
            new Coordinate(10.757933, 59.911491), // Oslo
            new Coordinate(9.0, 59.0),            // Via Larvik
            new Coordinate(8.0, 58.5),            // Via Arendal
            new Coordinate(5.733107, 58.969975)   // To Stavanger
        });

        // Create some points
        var oslo = GeoLocation.CreatePoint("Oslo", 10.757933, 59.911491);
        var stavanger = GeoLocation.CreatePoint("Stavanger", 5.733107, 58.969975);
        var bergen = GeoLocation.CreatePoint("Bergen", 5.324383, 60.397076);  // Not near route
        
        context.Locations.AddRange(route, oslo, stavanger, bergen);
        await context.SaveChangesAsync();

        // Act - Find points within 10km of the route
        var result = await context.Database.SqlQuery<GeoLocation>(FormattableStringFactory.Create(@"
            WITH route AS (
                SELECT ""Geometry""
                FROM locations
                WHERE ""Name"" = 'E18 Highway'
            )
            SELECT 
                l.""Id"",
                l.""Name"",
                l.""Geometry""
            FROM locations l, route
            WHERE ST_GeometryType(l.""Geometry"") = 'ST_Point'
            AND ST_DWithin(
                l.""Geometry""::geography,
                route.""Geometry""::geography,
                10000  -- 10km buffer
            )",
            Array.Empty<object>()
        )).ToListAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(l => l.Name == "Oslo");
        result.Should().Contain(l => l.Name == "Stavanger");
        result.Should().NotContain(l => l.Name == "Bergen");
    }

    [Fact]
    public async Task ClusterNearbyLocations_ShouldGroupClosePoints()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Create several points in Oslo area
        var oslo1 = GeoLocation.CreatePoint("Oslo Central", 10.757933, 59.911491);
        var oslo2 = GeoLocation.CreatePoint("Oslo East", 10.807933, 59.921491);
        var oslo3 = GeoLocation.CreatePoint("Oslo West", 10.707933, 59.901491);
        var bergen = GeoLocation.CreatePoint("Bergen", 5.324383, 60.397076); // Far from Oslo cluster

        context.Locations.AddRange(oslo1, oslo2, oslo3, bergen);
        await context.SaveChangesAsync();

        // Act - Cluster points that are within 5km of each other
        var clusters = await context.Database.SqlQuery<ClusterResult>(
            FormattableStringFactory.Create(@"
                WITH clustered_points AS (
                    SELECT 
                        l.*,
                        ST_ClusterDBSCAN(""Geometry"", 0.15, 1) 
                        OVER (
                            ORDER BY ST_Distance(
                                ""Geometry""::geography,
                                ST_SetSRID(ST_MakePoint(10.757933, 59.911491), 4326)::geography
                            )
                        ) as cluster_id
                    FROM locations l
                    WHERE ST_GeometryType(""Geometry"") = 'ST_Point'
                )
                SELECT 
                    cluster_id as ""ClusterId"",
                    COUNT(*) as ""PointCount"",
                    ST_Centroid(ST_Collect(""Geometry"")) as ""CenterPoint""
                FROM clustered_points
                GROUP BY cluster_id",
                Array.Empty<object>()
            )).ToListAsync();

        // Assert
        clusters.Should().HaveCount(2); // Should have 2 clusters (Oslo area and Bergen)
        clusters.Should().Contain(c => c.PointCount == 3); // Oslo cluster with 3 points
        clusters.Should().Contain(c => c.PointCount == 1); // Bergen cluster with 1 point
    }
    private record ClusterResult(
        int PointCount
    );
}