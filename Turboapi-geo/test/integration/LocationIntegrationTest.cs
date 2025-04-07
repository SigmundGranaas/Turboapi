using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
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
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;
using Turboapi.Infrastructure.Kafka;

namespace Turboapi_geo.test.integration;

public class LocationControllerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly KafkaContainer _kafka;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private readonly string _projectRoot;
    private const string TOPIC_NAME = "location-events";
    private const string CONSUMER_GROUP_POSITION = "location-group-update-position";
    private const string CONSUMER_GROUP_DISPLAY = "location-group-update-display";
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
    
        /// <summary>
    /// Waits for a specific condition to be met in the database
    /// </summary>
    /// <typeparam name="T">The type of result expected</typeparam>
    /// <param name="serviceProvider">The service provider to resolve dependencies</param>
    /// <param name="predicate">The condition to check</param>
    /// <param name="timeout">Optional timeout (defaults to 5 seconds)</param>
    /// <param name="pollInterval">Optional poll interval (defaults to 100ms)</param>
    /// <returns>The result of the predicate when the condition is met</returns>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout period</exception>
    public static async Task<T> WaitForDatabase<T>(
        IServiceProvider serviceProvider,
        Func<LocationReadContext, Task<T>> predicate,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
        where T : class
    {
        timeout ??= TimeSpan.FromSeconds(5);
        pollInterval ??= TimeSpan.FromMilliseconds(100);
        
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LocationReadContext>();
            
            var result = await predicate(dbContext);
            
            if (result != null)
            {
                return result;
            }
            
            await Task.Delay(pollInterval.Value);
        }
        
        throw new TimeoutException($"Database condition not met after {timeout.Value.TotalSeconds} seconds");
    }
    
    /// <summary>
    /// Waits for a specific entity to exist in the database
    /// </summary>
    /// <typeparam name="TEntity">The entity type</typeparam>
    /// <param name="serviceProvider">The service provider</param>
    /// <param name="predicate">The condition to check</param>
    /// <param name="timeout">Optional timeout</param>
    /// <returns>The entity when found</returns>
    public static Task<TEntity> WaitForEntity<TEntity>(
        IServiceProvider serviceProvider,
        Expression<Func<TEntity, bool>> predicate,
        TimeSpan? timeout = null)
        where TEntity : class
    {
        return WaitForDatabase(
            serviceProvider,
            async dbContext => await dbContext.Set<TEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(predicate),
            timeout);
    }
    
    /// <summary>
    /// Waits for a location to exist in the database by ID
    /// </summary>
    /// <param name="serviceProvider">The service provider</param>
    /// <param name="locationId">The location ID to wait for</param>
    /// <param name="timeout">Optional timeout</param>
    /// <returns>The location when found</returns>
    public static Task<LocationReadEntity> WaitForLocation(
        IServiceProvider serviceProvider,
        Guid locationId,
        TimeSpan? timeout = null)
    {
        return WaitForEntity<LocationReadEntity>(
            serviceProvider,
            location => location.Id == locationId,
            timeout);
    }
    
    /// <summary>
    /// Waits for a location to be updated with specific criteria
    /// </summary>
    /// <param name="serviceProvider">The service provider</param>
    /// <param name="locationId">The location ID</param>
    /// <param name="validateLocation">Function to validate location properties have been updated</param>
    /// <param name="timeout">Optional timeout</param>
    /// <returns>The location when criteria is met</returns>
    public static Task<LocationReadEntity> WaitForLocationUpdate(
        IServiceProvider serviceProvider,
        Guid locationId,
        Func<LocationReadEntity, bool> validateLocation,
        TimeSpan? timeout = null)
    {
        return WaitForDatabase(
            serviceProvider,
            async dbContext =>
            {
                var location = await dbContext.Locations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.Id == locationId);
                
                if (location != null && validateLocation(location))
                {
                    return location;
                }
                
                return null;
            },
            timeout);
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
                        d.ServiceType == typeof(KafkaConsumer<>) ||
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
        
                    services.AddSingleton(kafkaSettings); // Add as singleton for easy access in tests
                    services.Configure<KafkaSettings>(options =>
                    {
                        options.BootstrapServers = kafkaSettings.BootstrapServers;
                        options.LocationEventsTopic = kafkaSettings.LocationEventsTopic;
                    });

                    // Register the topic initializer
                    services.AddSingleton<IKafkaTopicInitializer, KafkaTopicInitializer>();
        
                    // Register event infrastructure
                    services.AddSingleton<IEventWriter, KafkaEventWriter>();
                    
                    services.AddKafkaConsumer<LocationPositionChanged, LocationPositionChangedHandler>(
                        TOPIC_NAME,
                        CONSUMER_GROUP_POSITION);
        
                    services.AddKafkaConsumer<LocationDisplayInformationChanged, LocationDisplayInformationChangedHandler>(
                        TOPIC_NAME,
                        CONSUMER_GROUP_DISPLAY);
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

    // New method to wait for Kafka message processing to complete
    private async Task WaitForKafkaProcessing(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        
        using var scope = _factory!.Services.CreateScope();
        
        // Wait for position updates
        var positionWaiter = KafkaMessageWaiter.FromServices(
            scope.ServiceProvider,
            TOPIC_NAME,
            CONSUMER_GROUP_POSITION
        );
        
        var result = await positionWaiter.WaitForMessagesProcessed(timeout.Value);
        if (!result)
        {
            throw new TimeoutException($"Timed out waiting for position messages to be processed after {timeout.Value.TotalSeconds} seconds");
        }
        
        // Wait for display updates
        var displayWaiter = KafkaMessageWaiter.FromServices(
            scope.ServiceProvider,
            TOPIC_NAME,
            CONSUMER_GROUP_DISPLAY
        );
        
        result = await displayWaiter.WaitForMessagesProcessed(timeout.Value);
        if (!result)
        {
            throw new TimeoutException($"Timed out waiting for display messages to be processed after {timeout.Value.TotalSeconds} seconds");
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

    var locationData = new LocationData(13.404954, 52.520008);
    var display = new DisplayInformationData("Location", null, null);

    var request = new CreateLocationRequest(
        locationData,
        display
    );
    
    // Act
    var response = await _client!.PostAsJsonAsync("/api/geo/locations", request);

    // Assert
    response.IsSuccessStatusCode.Should().BeTrue();
    var locationId = await response.Content.ReadFromJsonAsync<CreateLocationResponse>();

    // Wait for the location to be created in the database
    var location = await WaitForLocation(
        _factory!.Services,
        locationId.Id);
    
    // Verify location properties
    location.Should().NotBeNull($"Location {locationId.Id} was not found in read model");
    location.OwnerId.Should().Be(ownerId.ToString());
    location.Geometry.X.Should().Be(request.location.Longitude);
    location.Geometry.Y.Should().Be(request.location.Latitude);
}

[Fact]
public async Task UpdateLocation_ShouldUpdateDatabase_AndPublishEvent()
{
    var ownerId = Uuid7.NewUuid7();
    AddAuthHeader(ownerId.ToString());

    var locationData = new LocationData(13.404954, 52.520008);
    var display = new DisplayInformationData("Location", null, null);

    var request = new CreateLocationRequest(
        locationData,
        display
    );

    var createResponse = await _client!.PostAsJsonAsync("/api/geo/locations", request);
    var locationId = await createResponse.Content.ReadFromJsonAsync<CreateLocationResponse>();

    // Wait for the location to be created in the database
    await WaitForLocation(
        _factory!.Services,
        locationId.Id);
    
    // Update the location
    var updateRequest = new UpdateLocationRequest(
        new LocationData(20.405, 15.520),
        "Updated Location Name"
    );

    // Act
    var response = await _client!.PutAsJsonAsync(
        $"/api/geo/locations/{locationId.Id}/position", 
        updateRequest
    );

    // Assert
    response.IsSuccessStatusCode.Should().BeTrue();
    
    // Wait for the location to be updated in the database with specific criteria
    var updatedLocation = await WaitForLocationUpdate(
        _factory!.Services,
        locationId.Id,
        location => 
            location.Name == updateRequest.Name
    );
    
    // Verify final state explicitly
    updatedLocation.Should().NotBeNull();
    updatedLocation.Geometry.X.Should().BeApproximately(updateRequest.Location!.Longitude, 0.01);
    updatedLocation.Geometry.Y.Should().BeApproximately(updateRequest.Location.Latitude, 0.01);
    updatedLocation.Name.Should().Be(updateRequest.Name);
}
}