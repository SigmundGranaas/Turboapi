using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.Kafka;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Xunit;
using FluentAssertions;
using GeoSpatial.Domain.Events;
using Microsoft.Extensions.Options;
using Turboapi_geo.controller;
using Turboapi_geo.data;
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

    public LocationControllerIntegrationTests()
    {
        // Setup PostgreSQL with PostGIS
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:17-master")
            .WithDatabase("geodb")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        // Setup Kafka
        _kafka = new KafkaBuilder()
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

        // Setup Kafka topic
        await InitializeKafkaTopic();

        // Setup WebApplicationFactory after containers are ready
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                
                builder.ConfigureServices(services =>
                {
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
                        d.ServiceType == typeof(IEventPublisher) ||
                        d.ServiceType == typeof(IEventWriter) ||
                        d.ServiceType == typeof(IEventSubscriber)).ToList();
                    
                    foreach (var desc in descriptorsToRemove)
                    {
                        services.Remove(desc);
                    }
                    
                    KafkaSettings kafkaSettings = new KafkaSettings();
                    kafkaSettings.BootstrapServers = _kafka.GetBootstrapAddress();
                    kafkaSettings.LocationEventsTopic = TOPIC_NAME;
                    
                    services.AddSingleton(Options.Create(kafkaSettings));
                    
                    ILoggerFactory factory = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory();
                    
                    // Register interfaces with their implementations
                    services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
                    services.AddSingleton<IEventWriter, KafkaEventStore>();
                    
                    // Register logger factory and loggers
                    services.AddSingleton(factory);
                    services.AddSingleton(factory.CreateLogger<KafkaEventPublisher>());
                    services.AddSingleton(factory.CreateLogger<KafkaEventStore>());
                    services.AddSingleton(factory.CreateLogger<KafkaEventSubscriber>());
                    
                    services.AddSingleton<KafkaEventSubscriber>();
                    services.AddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<KafkaEventSubscriber>());
                    services.AddHostedService(sp => sp.GetRequiredService<KafkaEventSubscriber>());

                   // Then register LocationReadModelUpdater as hosted service
                    services.AddHostedService<LocationReadModelUpdater>();
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
        timeout ??= TimeSpan.FromSeconds(10);
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

    private async Task InitializeKafkaTopic()
    {
        using var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress()
        }).Build();

        try
        {
            await adminClient.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name = TOPIC_NAME,
                    ReplicationFactor = 1,
                    NumPartitions = 1
                }
            });
        }
        catch (CreateTopicsException e) when (e.Results.Any(r => 
            r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // Topic exists, which is fine
        }
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

    [Fact]
    public async Task CreateLocation_ShouldPersistToDatabase_AndPublishEvent()
    {
        // Arrange
        var createRequest = new CreateLocationRequest(
            "integration_test_owner",
            13.404954,
            52.520008
        );

        // Act
        var response = await _client!.PostAsJsonAsync("/api/locations", createRequest);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var locationId = await response.Content.ReadFromJsonAsync<Guid>();

        // Wait for the event to be processed and the read model to be updated
        var location = await WaitForCondition(async provider =>
        {
            var context = provider.GetRequiredService<LocationReadContext>();
            return await context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == locationId);
        }, timeoutMessage: $"Location {locationId} was not found in read model");
        
        location!.OwnerId.Should().Be(createRequest.OwnerId);
        location.Geometry.X.Should().Be(createRequest.Longitude);
        location.Geometry.Y.Should().Be(createRequest.Latitude);
        
    }

    [Fact]
    public async Task UpdateLocation_ShouldUpdateDatabase_AndPublishEvent()
    {
        // Arrange - First create a location
        var createRequest = new CreateLocationRequest(
            "integration_test_owner",
            13.404954,
            52.520008
        );
        var createResponse = await _client!.PostAsJsonAsync("/api/locations", createRequest);
        var locationId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateRequest = new UpdateLocationPositionRequest(
            13.405,
            52.520
        );

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/locations/{locationId}/position", 
            updateRequest
        );

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocationReadContext>();
        
        // Verify database
        var location = await context.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == locationId);

        location.Should().NotBeNull();
        location!.Geometry.X.Should().Be(updateRequest.Longitude);
        location.Geometry.Y.Should().Be(updateRequest.Latitude);
    }
}