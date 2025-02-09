using System.Diagnostics;
using FluentAssertions;
using GeoSpatial.Domain.Events;
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

public class LocationLocationConsumerIntegrationTest: IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly KafkaContainer _kafka;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private readonly string _projectRoot;
    private const string TOPIC_NAME = "location";
    private const string COMMAND_TOPIC = "location.create_command";

    private string? _jwtSecret;

    public LocationLocationConsumerIntegrationTest()
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
        timeout ??= TimeSpan.FromSeconds(4);
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

    [Fact]
    public async Task CreateLocation_ShouldPersistToDatabase_AndPublishEvent()
    {
        var ownerId = Uuid7.NewUuid7();
        var positionId = Guid.Parse(Uuid7.NewUuid7().ToString());
        var activityId = Uuid7.NewUuid7();
        var pos = new LatLng
        {
            Latitude = 12.5,
            Longitude = 56.3
        };

        var @event = new CreatePositionEvent(positionId, pos, activityId, ownerId);

        var options = Options.Create(new KafkaSettings
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            LocationEventsTopic = COMMAND_TOPIC
        });
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var writerLogger = loggerFactory.CreateLogger<KafkaEventWriter>();
        var initLogger = loggerFactory.CreateLogger<KafkaTopicInitializer>();
        var consumerLogger = loggerFactory.CreateLogger<KafkaLocationConsumer>();

        var publisher = new KafkaEventWriter(new KafkaTopicInitializer(options, initLogger), options, writerLogger);
        var consumer = new KafkaLocationConsumer(new KafkaTopicInitializer(options, initLogger), options, (IServiceScopeFactory)_factory.Services.GetService(typeof(IServiceScopeFactory)), consumerLogger);
        
        var token = new CancellationTokenSource();
        await consumer.StartAsync(token.Token);
        
        await publisher.AppendEvents(new List<CreatePositionEvent> { @event });
        
        // Wait for the event to be processed and the read model to be updated
        var location = await WaitForCondition(async provider =>
        {
            var context = provider.GetRequiredService<LocationReadContext>();
            return await context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id== positionId);
        }, timeoutMessage: $"Location {positionId} was not found in read model");
        
        
        location!.OwnerId.Should().Be(ownerId.ToString());
        location.Geometry.X.Should().Be(pos.Longitude);
        location.Geometry.Y.Should().Be(pos.Latitude);
        
        await token.CancelAsync();
    }
}
