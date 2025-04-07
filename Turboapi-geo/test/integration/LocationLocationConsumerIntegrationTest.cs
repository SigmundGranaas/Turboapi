using System.Diagnostics;
using FluentAssertions;
using Medo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.domain.value;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;
using Xunit;

namespace Turboapi_geo.test.integration;


public class LocationConsumerIntegrationTest : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly KafkaContainer _kafka;
    private WebApplicationFactory<Program>? _factory;
    private readonly string _projectRoot;
    
    public LocationConsumerIntegrationTest()
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

        // Configure Kafka settings for test
        var kafkaSettings = new KafkaSettings
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            LocationEventsTopic = "location-events"
        };
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var topicInitializer = new KafkaTopicInitializer(
            Options.Create(kafkaSettings), 
            loggerFactory.CreateLogger<KafkaTopicInitializer>());

        await topicInitializer.EnsureTopicExists("location.create_command");
        await topicInitializer.EnsureTopicExists("location-events");

        
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureServices((context, services) =>
                {
                    // Replace DbContext
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

                    // Remove existing Kafka-related services
                    var descriptorsToRemove = services.Where(d => 
                        d.ServiceType == typeof(KafkaSettings))
                        .ToList();
                    
                    foreach (var desc in descriptorsToRemove)
                    {
                        services.Remove(desc);
                    }

  
        
                    services.Configure<KafkaSettings>(options =>
                    {
                        options.BootstrapServers = kafkaSettings.BootstrapServers;
                    });
                });
            });
        var client = _factory.CreateClient();
    
        // Verify services are ready
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LocationReadContext>();
        await context.Database.CanConnectAsync();
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
        Func<Task<T?>> condition,
        TimeSpan? timeout = null,
        string? timeoutMessage = null)
    {
        timeout ??= TimeSpan.FromSeconds(4);
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            var result = await condition();

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

    [Fact]
    public async Task CreateLocation_ShouldPersistToDatabase_AndPublishEvent()
    {
        // Create a publisher for testing
        var options = Options.Create(new KafkaSettings
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            LocationEventsTopic = "location.create_command"
        });
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var writerLogger = loggerFactory.CreateLogger<KafkaEventWriter>();
        var initLogger = loggerFactory.CreateLogger<KafkaTopicInitializer>();

        var topicInitializer = new KafkaTopicInitializer(options, initLogger);
        var publisher = new KafkaEventWriter(topicInitializer, options, writerLogger);
        
        using var scope = _factory!.Services.CreateScope();

        // Arrange
        var ownerId = Uuid7.NewUuid7();
        var positionId = Guid.Parse(Uuid7.NewUuid7().ToString());
        var activityId = Uuid7.NewUuid7();
        var pos = new LatLng
        {
            Latitude = 12.5,
            Longitude = 56.3
        };

        var @event = new CreatePositionEvent(positionId, pos, activityId, ownerId);
        
        // Act
        await publisher.AppendEvents(new List<CreatePositionEvent> { @event });
        
        // Assert - Wait for the event to be processed and the read model to be updated
        var location = await WaitForCondition(async () =>
        {
            var context = scope.ServiceProvider.GetRequiredService<LocationReadContext>();
            return await context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == positionId);
        }, timeoutMessage: $"Location {positionId} was not found in read model");
        
        // Verify the results
        location.Should().NotBeNull();
        location.OwnerId.Should().Be(ownerId.ToString());
        location.Geometry.X.Should().Be(pos.Longitude);
        location.Geometry.Y.Should().Be(pos.Latitude);
    }
}
