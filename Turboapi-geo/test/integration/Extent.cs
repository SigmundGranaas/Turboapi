using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Testcontainers.PostgreSql;
using Xunit;

namespace Turboapi_geo.test.integration;

public class SpatialExtentTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;
    private string _connectionString;

    public SpatialExtentTests()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:17-master")
            .WithDatabase("geo")
            .WithUsername("postgres")
            .WithPassword("your_password")
            .Build();
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

    public class GeoLocation
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public Point Geometry { get; private set; }

        private GeoLocation() { }

        public static GeoLocation Create(string name, double longitude, double latitude)
        {
            var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
            return new GeoLocation
            {
                Id = Guid.NewGuid(),
                Name = name,
                Geometry = geometryFactory.CreatePoint(new Coordinate(longitude, latitude))
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
                    .HasColumnType("geometry(Point, 4326)");
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

    private record LocationInExtent(
        Guid Id,
        string Name,
        Point Geometry,
        bool IsWithinExtent
    );

    [Fact]
    public async Task FindLocationsWithinExtent_ShouldReturnCorrectLocations()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Create test locations across Norway
        var oslo = GeoLocation.Create("Oslo", 10.757933, 59.911491);
        var bergen = GeoLocation.Create("Bergen", 5.324383, 60.397076);
        var trondheim = GeoLocation.Create("Trondheim", 10.396466, 63.430515);
        var tromso = GeoLocation.Create("Tromsø", 18.95508, 69.649208);
        var stavanger = GeoLocation.Create("Stavanger", 5.733107, 58.969975);

        context.Locations.AddRange(oslo, bergen, trondheim, tromso, stavanger);
        await context.SaveChangesAsync();

        // Define a bounding box that covers southern Norway (including Oslo, Bergen, and Stavanger)
        var minLon = 4.0;  // Western boundary
        var minLat = 58.0; // Southern boundary
        var maxLon = 12.0; // Eastern boundary
        var maxLat = 62.0; // Northern boundary

        // Act - Find locations within the bounding box
        var result = await context.Database.SqlQuery<LocationInExtent>(FormattableStringFactory.Create(@"
            SELECT 
                l.""Id"",
                l.""Name"",
                l.""Geometry"",
                ST_Contains(
                    ST_MakeEnvelope({0}, {1}, {2}, {3}, 4326),
                    l.""Geometry""
                ) as ""IsWithinExtent""
            FROM locations l
            ORDER BY l.""Name""",
            minLon, minLat, maxLon, maxLat
        )).ToListAsync();

        // Assert
        result.Should().HaveCount(5); // Should return all locations with their containment status

        // Locations that should be within the extent
        result.Single(r => r.Name == "Oslo").IsWithinExtent.Should().BeTrue();
        result.Single(r => r.Name == "Bergen").IsWithinExtent.Should().BeTrue();
        result.Single(r => r.Name == "Stavanger").IsWithinExtent.Should().BeTrue();

        // Locations that should be outside the extent
        result.Single(r => r.Name == "Trondheim").IsWithinExtent.Should().BeFalse();
        result.Single(r => r.Name == "Tromsø").IsWithinExtent.Should().BeFalse();

        // Act 2 - Get only the locations within the extent using WHERE clause
        var locationsInExtent = await context.Database.SqlQuery<LocationInExtent>(FormattableStringFactory.Create(@"
            SELECT 
                l.""Id"",
                l.""Name"",
                l.""Geometry"",
                TRUE as ""IsWithinExtent""
            FROM locations l
            WHERE ST_Contains(
                ST_MakeEnvelope({0}, {1}, {2}, {3}, 4326),
                l.""Geometry""
            )
            ORDER BY l.""Name""",
            minLon, minLat, maxLon, maxLat
        )).ToListAsync();

        // Assert 2
        locationsInExtent.Should().HaveCount(3); // Should only return locations within the extent
        locationsInExtent.Should().AllSatisfy(l => l.IsWithinExtent.Should().BeTrue());
        locationsInExtent.Select(l => l.Name).Should().BeEquivalentTo(
            new[] { "Bergen", "Oslo", "Stavanger" }
        );
    }

    [Fact]
    public async Task FindLocationsWithinExtent_EmptyExtent_ShouldReturnNoLocations()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var oslo = GeoLocation.Create("Oslo", 10.757933, 59.911491);
        context.Locations.Add(oslo);
        await context.SaveChangesAsync();

        // Define an extent far from any test locations (in the Pacific Ocean)
        var minLon = -170.0;
        var minLat = 0.0;
        var maxLon = -160.0;
        var maxLat = 10.0;

        // Act
        var result = await context.Database.SqlQuery<LocationInExtent>(FormattableStringFactory.Create(@"
            SELECT 
                l.""Id"",
                l.""Name"",
                l.""Geometry"",
                ST_Contains(
                    ST_MakeEnvelope({0}, {1}, {2}, {3}, 4326),
                    l.""Geometry""
                ) as ""IsWithinExtent""
            FROM locations l",
            minLon, minLat, maxLon, maxLat
        )).ToListAsync();

        // Assert
        result.Should().HaveCount(1);
        result.Single().IsWithinExtent.Should().BeFalse();
    }
}