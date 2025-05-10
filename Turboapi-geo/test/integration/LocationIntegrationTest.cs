using System.IdentityModel.Tokens.Jwt;
// using System.Linq.Expressions; // No longer strictly needed for these tests' assertions
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore; // Still needed for DB setup in InitializeAsync
using Testcontainers.PostgreSql;
using Testcontainers.Kafka;

using Xunit;
using FluentAssertions;
using GeoSpatial.Domain.Events;
using Medo;
using Microsoft.IdentityModel.Tokens;
using Turboapi_geo.controller.request;
using Turboapi_geo.controller.response;
// using Turboapi_geo.data.model; // No longer directly used in test methods
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;
using Turboapi.Infrastructure.Kafka;
using System.Diagnostics;
using System.Net.Http.Json; // Required for ReadFromJsonAsync and PostAsJsonAsync

namespace Turboapi_geo.test.integration;

public class LocationControllerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly KafkaContainer _kafka;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private readonly string _projectRoot;
    private const string TOPIC_NAME = "location-events";
    private const string CONSUMER_GROUP = "location-group-update";
    private string? _jwtSecret;

    public LocationControllerIntegrationTests()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:17-3.5-alpine")
            .WithDatabase("geodb")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:6.2.10")
            .WithPortBinding(9092, true)
            .Build();

        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir.FullName, "db")))
        {
            currentDir = currentDir.Parent;
        }
        _projectRoot = currentDir?.FullName ?? throw new Exception("Could not find project root");
    } 
    
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _kafka.StartAsync();
        await RunFlywayMigrations();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                
                builder.ConfigureServices((context, services) =>
                {
                    _jwtSecret = context.Configuration["Jwt:Key"];
                    if (string.IsNullOrEmpty(_jwtSecret))
                    {
                        throw new Exception("JWT secret not found in configuration");
                    }
                    
                    var descriptor = services.SingleOrDefault(d => 
                        d.ServiceType == typeof(DbContextOptions<LocationReadContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddDbContext<LocationReadContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString(), x => x.UseNetTopologySuite()));

                    var descriptorsToRemove = services.Where(d => 
                        d.ServiceType == typeof(IEventWriter) ||
                        d.ServiceType == typeof(KafkaSettings) ||
                        d.ServiceType == typeof(IKafkaTopicInitializer) ||
                        d.ServiceType == typeof(KafkaConsumer<>) ||
                        d.ServiceType == typeof(IEventSubscriber)).ToList();
                    foreach (var desc in descriptorsToRemove)
                    {
                        services.Remove(desc);
                    }
                    
                    var kafkaSettings = new KafkaSettings
                    {
                        BootstrapServers = _kafka.GetBootstrapAddress(),
                        LocationEventsTopic = TOPIC_NAME
                    };
                    services.AddSingleton(kafkaSettings);
                    services.Configure<KafkaSettings>(options =>
                    {
                        options.BootstrapServers = kafkaSettings.BootstrapServers;
                        options.LocationEventsTopic = kafkaSettings.LocationEventsTopic;
                    });
                    services.AddSingleton<IKafkaTopicInitializer, KafkaTopicInitializer>();
                    services.AddSingleton<IEventWriter, KafkaEventWriter>();
                    services.AddKafkaConsumer<LocationUpdated, LocationUpdatedHandler>(TOPIC_NAME, CONSUMER_GROUP);
                });
            });
        _client = _factory.CreateClient();
    }

    private async Task RunFlywayMigrations()
    {
        var host = _postgres.Hostname;
        var port = _postgres.GetMappedPublicPort(5432);
        var connString = $"jdbc:postgresql://{host}:{port}/geodb?user=postgres&password=postgres";
        var migrationsPath = Path.Combine(_projectRoot, "db", "migrations");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "flyway",
                Arguments = $"migrate -url=\"{connString}\" -locations=filesystem:{migrationsPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _projectRoot
            }
        };
        process.Start();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Flyway migration failed: {error}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory != null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _kafka.DisposeAsync();
    }
    
    private void AddAuthHeader(string userId)
    {
        var token = GenerateJwtToken(userId);
        _client!.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
    
    private string GenerateJwtToken(string userId)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSecret!);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
            Issuer = "turbo-auth",
            Audience = "turbo-client",
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public async Task CreateLocation_ShouldPersistToDatabase()
    {
        var ownerId = Uuid7.NewUuid7();
        AddAuthHeader(ownerId.ToString());

        var geometry = new GeometryData
        {
            Longitude = 13.404954,
            Latitude = 52.520008
        };
    
        var display = new DisplayData
        {
            Name = "API Created Location",
            Description = "Description for API created location",
            Icon = "icon_api_create"
        };

        var request = new CreateLocationRequest
        {
            Geometry = geometry,
            Display = display
        };
    
        // Act: Create the location
        var postResponse = await _client!.PostAsJsonAsync("/api/geo/locations", request);

        // Assert: Check POST response
        postResponse.EnsureSuccessStatusCode(); // Fail fast if POST fails
        var createdLocationResponse = await postResponse.Content.ReadFromJsonAsync<LocationResponse>();
        createdLocationResponse.Should().NotBeNull();
        createdLocationResponse.Id.Should().NotBeEmpty();

        // Act: Fetch the created location via API GET endpoint
        // Auth header is already set on _client by AddAuthHeader
        var getResponse = await _client!.GetAsync($"/api/geo/locations/{createdLocationResponse.Id}");

        // Assert: Check GET response and its content
        getResponse.EnsureSuccessStatusCode(); // Fail fast if GET fails
        var fetchedLocation = await getResponse.Content.ReadFromJsonAsync<LocationResponse>();
    
        fetchedLocation.Should().NotBeNull();
        fetchedLocation!.Id.Should().Be(createdLocationResponse.Id);
        // Assuming LocationResponse has OwnerId. If not, this assertion needs to be removed/adapted.
        fetchedLocation.Geometry.Longitude.Should().BeApproximately(request.Geometry.Longitude, 0.000001);
        fetchedLocation.Geometry.Latitude.Should().BeApproximately(request.Geometry.Latitude, 0.000001);
        fetchedLocation.Display.Name.Should().Be(request.Display.Name);
        fetchedLocation.Display.Description.Should().Be(request.Display.Description);
        fetchedLocation.Display.Icon.Should().Be(request.Display.Icon);
    }

    [Fact]
    public async Task UpdateLocation_ShouldUpdateDatabase()
    {
        var ownerId = Uuid7.NewUuid7();
        AddAuthHeader(ownerId.ToString());

        // Arrange: Create a location first
        var initialGeometry = new GeometryData { Longitude = 13.404954, Latitude = 52.520008 };
        var initialDisplay = new DisplayData { Name = "Original API Location", Description = "Original Desc", Icon = "original_icon" };
        var createRequest = new CreateLocationRequest { Geometry = initialGeometry, Display = initialDisplay };

        var createResponse = await _client!.PostAsJsonAsync("/api/geo/locations", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var createdLocation = await createResponse.Content.ReadFromJsonAsync<LocationResponse>();
        createdLocation.Should().NotBeNull();
        var locationId = createdLocation!.Id; // Null forgiveness as NotBeNull is checked

        // Arrange: Prepare update request
        var updatedGeometry = new GeometryData { Longitude = 20.405, Latitude = 15.520 };
        var updatedDisplayChangeset = new DisplayChangeset 
        { 
            Name = "Updated API Location Name", 
            Description = "Updated API description", 
            Icon = "updated_api_icon" 
        };
        var updateRequest = new UpdateLocationRequest { Geometry = updatedGeometry, Display = updatedDisplayChangeset };

        // Act: Update the location
        // Auth header is already set on _client
        var putResponse = await _client!.PutAsJsonAsync($"/api/geo/locations/{locationId}", updateRequest);

        // Assert: Check PUT response
        putResponse.EnsureSuccessStatusCode();
        
        // Act: Fetch the updated location via API GET endpoint
        var getResponse = await _client!.GetAsync($"/api/geo/locations/{locationId}");

        // Assert: Check GET response and its content
        getResponse.EnsureSuccessStatusCode();
        var fetchedUpdatedLocation = await getResponse.Content.ReadFromJsonAsync<LocationResponse>();
        
        fetchedUpdatedLocation.Should().NotBeNull();
        fetchedUpdatedLocation!.Id.Should().Be(locationId);
        fetchedUpdatedLocation.Geometry.Longitude.Should().BeApproximately(updateRequest.Geometry.Longitude, 0.000001);
        fetchedUpdatedLocation.Geometry.Latitude.Should().BeApproximately(updateRequest.Geometry.Latitude, 0.000001);
        fetchedUpdatedLocation.Display.Name.Should().Be(updateRequest.Display.Name);
        fetchedUpdatedLocation.Display.Description.Should().Be(updateRequest.Display.Description);
        fetchedUpdatedLocation.Display.Icon.Should().Be(updateRequest.Display.Icon);
    }
}