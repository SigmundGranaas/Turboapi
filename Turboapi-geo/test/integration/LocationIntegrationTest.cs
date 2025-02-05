using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.Kafka;

using Xunit;
using FluentAssertions;
using GeoSpatial.Domain.Events;
using Medo;
using Microsoft.IdentityModel.Tokens;
using Turboapi_geo.controller;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;

namespace Turboapi_geo.test.integration;

public class LocationControllerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly KafkaContainer _kafka;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private readonly string _projectRoot;
    private const string TOPIC_NAME = "location-events";
    private string? _jwtSecret;

    public LocationControllerIntegrationTests()
    {
        // Setup PostgreSQL with PostGIS
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:17-3.5-alpine")
            .WithDatabase("geodb")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        // Setup Kafka
        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:6.2.10")
            .WithName("kafka-integration")
            .WithPortBinding(9092, true)  // External port
            .Build();

        // Find project root
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null && !Directory.Exists(Path.Combine(currentDir.FullName, "db")))
        {
            currentDir = currentDir.Parent;
        }
        _projectRoot = currentDir?.FullName ?? throw new Exception("Could not find project root");
    }

    public async Task InitializeAsync()
    {
        // Start containers
        await _postgres.StartAsync();
        await _kafka.StartAsync();

        // Initialize database
        await RunFlywayMigrations();

        // Setup WebApplicationFactory after containers are ready
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                
                builder.ConfigureServices((context, services) =>
                {
                    // Get JWT secret from configuration
                    _jwtSecret = context.Configuration["Jwt:Key"];
                    
                    if (string.IsNullOrEmpty(_jwtSecret))
                    {
                        throw new Exception("JWT secret not found in configuration");
                    }

                    
                    // Replace default DbContext
                    var descriptor = services.SingleOrDefault(d => 
                        d.ServiceType == typeof(DbContextOptions<LocationReadContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContext<LocationReadContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString(),
                            x => x.UseNetTopologySuite()
                        )
                    );

                    // Remove old Default setup
                    var descriptorsToRemove = services.Where(d => 
                        d.ServiceType == typeof(IEventWriter) ||
                        d.ServiceType == typeof(KafkaSettings) ||
                        d.ServiceType == typeof(IKafkaTopicInitializer) ||
                        d.ServiceType == typeof(IEventSubscriber)).ToList();
                    
                    foreach (var desc in descriptorsToRemove)
                    {
                        services.Remove(desc);
                    }
                    
                    // Configure Kafka settings
                    var kafkaSettings = new KafkaSettings
                    {
                        BootstrapServers = _kafka.GetBootstrapAddress(),
                        LocationEventsTopic = TOPIC_NAME
                    };
        
                    services.Configure<KafkaSettings>(options =>
                    {
                        options.BootstrapServers = kafkaSettings.BootstrapServers;
                        options.LocationEventsTopic = kafkaSettings.LocationEventsTopic;
                    });

                    services.AddSingleton<IKafkaTopicInitializer, KafkaTopicInitializer>();
                    services.AddSingleton<IEventWriter, KafkaEventWriter>();
                    services.AddHostedService<KafkaLocationConsumer>();
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
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Flyway migration failed: {error}");
        }
    }

    private async Task<T?> WaitForCondition<T>(
        Func<IServiceProvider, Task<T?>> condition,
        TimeSpan? timeout = null,
        string? timeoutMessage = null)
    {
        timeout ??= TimeSpan.FromSeconds(1);
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            using var scope = _factory!.Services.CreateScope();
            var result = await condition(scope.ServiceProvider);
                
            if (result != null)
            {
                return result;
            }
            
            await Task.Delay(100); // Wait 100ms before next attempt
        }

        throw new TimeoutException(
            timeoutMessage ?? $"Condition not met after {timeout.Value.TotalSeconds} seconds");
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
        await _kafka.DisposeAsync();
    }
    
    // Helper method to add auth header to requests
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
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            ),
            Issuer = "turbo-auth",
            Audience = "turbo-client",
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }


    [Fact]
    public async Task CreateLocation_ShouldPersistToDatabase_AndPublishEvent()
    {
        var ownerId = Uuid7.NewUuid7();
        AddAuthHeader(ownerId.ToString());

        var createRequest = new CreateLocationRequest(
            13.404954,
            52.520008
        );
        
        // Act
        var response = await _client!.PostAsJsonAsync("/api/locations", createRequest);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var locationId = await response.Content.ReadFromJsonAsync<CreateLocationResponse>();

        // Wait for the event to be processed and the read model to be updated
        var location = await WaitForCondition(async provider =>
        {
            var context = provider.GetRequiredService<LocationReadContext>();
            return await context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == locationId.Id);
        }, timeoutMessage: $"Location {locationId} was not found in read model");
        
        location!.OwnerId.Should().Be(ownerId.ToString());
        location.Geometry.X.Should().Be(createRequest.Longitude);
        location.Geometry.Y.Should().Be(createRequest.Latitude);
    }

    [Fact]
    public async Task UpdateLocation_ShouldUpdateDatabase_AndPublishEvent()
    {
        var ownerId = Uuid7.NewUuid7();
        AddAuthHeader(ownerId.ToString());

        // Create initial location
        var createRequest = new CreateLocationRequest(
            13.404954,
            52.520008
        );

        var createResponse = await _client!.PostAsJsonAsync("/api/locations", createRequest);
        var locationId = await createResponse.Content.ReadFromJsonAsync<CreateLocationResponse>();

        // Wait for the event to be processed and the read model to be updated
         await WaitForCondition(async provider =>
            {
                var context = provider.GetRequiredService<LocationReadContext>();
                return await context.Locations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.Id == locationId.Id);
            }, timeoutMessage: $"Location {locationId} was not found in read model");
        
        // Update the location
        var updateRequest = new UpdateLocationPositionRequest(
            13.405,
            52.520
        );

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/locations/{locationId.Id}/position", 
            updateRequest
        );

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        // Wait for the update to be reflected in the read model

        // Verify final state
        using var scope = _factory!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocationReadContext>();
        
        var location = await context.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == locationId.Id);

        location.Should().NotBeNull();
        location!.Geometry.X.Should().BeApproximately(updateRequest.Longitude, 0.01);
        location.Geometry.Y.Should().BeApproximately(updateRequest.Latitude, 0.01);
    }
}