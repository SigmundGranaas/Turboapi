using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Testcontainers.PostgreSql;
using Xunit;

public class SpatialDistanceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;
    private string _connectionString;
    private readonly GeometryFactory _geometryFactory;

    public SpatialDistanceTests()
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

    private record LocationDistance(
        Guid Id,
        string Name,
        Point Geometry,
        double DistanceMeters
    );

    [Fact]
    public async Task FindLocationsWithinDistance_ShouldReturnCorrectLocations()
    {
        // Arrange
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Oslo coordinates
        var oslo = GeoLocation.Create("Oslo", 10.757933, 59.911491);
        // Bergen coordinates
        var bergen = GeoLocation.Create("Bergen", 5.324383, 60.397076);
        // Trondheim coordinates
        var trondheim = GeoLocation.Create("Trondheim", 10.396466, 63.430515);

        context.Locations.AddRange(oslo, bergen, trondheim);
        await context.SaveChangesAsync();

        // Act - Find locations within 310km of Oslo (should only find Oslo and Bergen)
        var result = await context.Database.SqlQuery<LocationDistance>(FormattableStringFactory.Create(@"
            WITH distances AS (
                SELECT 
                    l.""Id"",
                    l.""Name"",
                    l.""Geometry"",
                    ST_Distance(l.""Geometry""::geography, ST_SetSRID(ST_Point({0}, {1}), 4326)::geography) as ""DistanceMeters""
                FROM locations l
            )
            SELECT * FROM distances
            WHERE ""DistanceMeters"" <= {2}
            ORDER BY ""DistanceMeters""",
            oslo.Geometry.X,  // longitude
            oslo.Geometry.Y,  // latitude
            310_000.0        // 310km in meters
        )).ToListAsync();

        // Assert
        result.Should().HaveCount(2); // Should find Oslo and Bergen
        result.Should().Contain(d => d.Name == "Oslo");
        result.Should().Contain(d => d.Name == "Bergen");
        result.Should().NotContain(d => d.Name == "Trondheim");

        var osloResult = result.Single(d => d.Name == "Oslo");
        var bergenResult = result.Single(d => d.Name == "Bergen");

        osloResult.DistanceMeters.Should().BeApproximately(0, 0.1);
        bergenResult.DistanceMeters.Should().BeApproximately(306_497, 1000);

        // Act 2 - Get distances to all cities
        var allDistances = await context.Database.SqlQuery<LocationDistance>(FormattableStringFactory.Create(@"
            SELECT 
                l.""Id"",
                l.""Name"",
                l.""Geometry"",
                ST_Distance(l.""Geometry""::geography, ST_SetSRID(ST_Point({0}, {1}), 4326)::geography) as ""DistanceMeters""
            FROM locations l
            ORDER BY ""DistanceMeters""",
            oslo.Geometry.X,
            oslo.Geometry.Y
        )).ToListAsync();

        // Assert 2
        allDistances.Should().HaveCount(3);
        var bergenDistance = allDistances.Single(d => d.Name == "Bergen").DistanceMeters;
        var trondheimDistance = allDistances.Single(d => d.Name == "Trondheim").DistanceMeters;

        bergenDistance.Should().BeApproximately(306_497, 1000);    // ~306 km
        trondheimDistance.Should().BeApproximately(392_625, 1000); // ~392 km
    }
}