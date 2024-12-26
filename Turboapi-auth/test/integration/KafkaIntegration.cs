using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Turboapi.dto;
using Turboapi.infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using TurboApi.Data.Entity;
using Xunit;
using Testcontainers.Kafka;
using Turboapi.events;

namespace Turboapi.test.integration;

public class KafkaIntegrationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db;
    private readonly KafkaFixture _kafka;
    private readonly WebAppFixture _webApp;

    public KafkaIntegrationTests()
    {
        _db = new DatabaseFixture();
        _kafka = new KafkaFixture();
        
        _webApp = new WebAppFixture(services =>
        {
            // Replace real DbContext with test double
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(AuthDbContext));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }
            services.AddScoped<AuthDbContext>(sp => _db.CreateContext());

            // Use real Kafka publisher
            var kafkaDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEventPublisher));
            if (kafkaDescriptor != null)
            {
                services.Remove(kafkaDescriptor);
            }
            services.AddSingleton(_kafka.EventPublisher);
        });
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await _kafka.InitializeAsync();
        await _webApp.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _kafka.DisposeAsync();
        await _webApp.DisposeAsync();
    }

    [Fact]
    public async Task UserRegistration_PublishesEvent()
    {
        using var consumer = _kafka.CreateConsumer();
        
        // Wait briefly to ensure consumer is ready
        await Task.Delay(1000);

        // Register a new user
        var email = "kafka-test@example.com";
        var response = await _webApp.Client.PostAsJsonAsync("/api/auth/register", 
            new RegisterRequest(email, "SecurePass123!", "SecurePass123!"));
            
        // Verify registration was successful
        Assert.True(response.IsSuccessStatusCode, 
            $"Registration failed with status {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        // Verify the event was published with more detailed error message
        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(10));
        Assert.NotNull(consumeResult);
        
        var eventData = JsonSerializer.Deserialize<UserCreatedEvent>(consumeResult.Message.Value);
        Assert.NotNull(eventData);
        Assert.Equal(email, eventData.Email);
    }
}

public class WebAppFixture : IAsyncDisposable 
{ 
    private readonly Action<IServiceCollection> _configureServices;
    public WebApplicationFactory<Program> Factory { get; private set; }
    public HttpClient Client { get; private set; }

    public WebAppFixture(Action<IServiceCollection> configureServices)
    {
        _configureServices = configureServices;
    }

    public async Task InitializeAsync()
    {
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                
                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                });
                
                builder.ConfigureServices(_configureServices);
            });

        Client = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory != null)
        {
            await Factory.DisposeAsync();
        }
        Client?.Dispose();
    }
}


public class DatabaseFixture : IAsyncDisposable
{
    private readonly DbContextOptions<AuthDbContext> _options;

    public DatabaseFixture()
    {
        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .EnableSensitiveDataLogging()
            .Options;
    }

    public AuthDbContext CreateContext()
    {
        var context = new AuthDbContext(_options);
        
        // Override OnModelCreating to add our custom configurations
        var modelBuilder = new ModelBuilder();
        
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Configure default values for timestamps
            var timestampProperties = entityType.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) && 
                            (p.Name == "CreatedAt" || p.Name.EndsWith("At")));
                           
            foreach (var property in timestampProperties)
            {
                property.SetDefaultValueSql("CURRENT_TIMESTAMP");
            }
        }

        return context;
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}

public class KafkaFixture : IAsyncDisposable
{
    public KafkaContainer Container { get; }
    public IEventPublisher EventPublisher { get; private set; }
    private readonly ILogger<KafkaFixture> _logger;
    public const string TopicName = "authentication-events"; // Match the topic name used in KafkaSettings
    
    public KafkaFixture()
    {
        Container = new KafkaBuilder()
            .WithName("kafka-test")
            .WithPortBinding(9092, true)
            .Build();
            
        var factory = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory();
        _logger = factory.CreateLogger<KafkaFixture>();
    }

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        
        // Create the topic before initializing publisher
        using (var adminClient = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = Container.GetBootstrapAddress()
        }).Build())
        {
            try
            {
                await adminClient.CreateTopicsAsync(new TopicSpecification[]
                {
                    new TopicSpecification
                    {
                        Name = TopicName,
                        ReplicationFactor = 1,
                        NumPartitions = 1
                    }
                });
            }
            catch (CreateTopicsException e) when (e.Results.Select(r => r.Error.Code)
                .All(code => code == ErrorCode.TopicAlreadyExists))
            {
                // Topic already exists, which is fine
            }
        }

        KafkaSettings kafkaSettings = new KafkaSettings();
        kafkaSettings.BootstrapServers = Container.GetBootstrapAddress();
        kafkaSettings.UserAccountsTopic = TopicName; // Use the same topic name consistently
        
        var settings = Options.Create(kafkaSettings);
        
        ILoggerFactory factory = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory();
        ILogger<KafkaEventPublisher> logger = factory.CreateLogger<KafkaEventPublisher>();
        EventPublisher = new KafkaEventPublisher(
            settings,
            logger
            );
    }

    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync();
    }

    public IConsumer<string, string> CreateConsumer()
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = Container.GetBootstrapAddress(),
            GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = true,
            MaxPollIntervalMs = 300000 // 5 minutes
        };

        _logger.LogInformation("Creating consumer for topic: {Topic} with bootstrap servers: {Servers}", 
            TopicName, Container.GetBootstrapAddress());

        var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Error}", e.Reason))
            .Build();
            
        consumer.Subscribe(TopicName);
        return consumer;
    }
}

public class TestHelper
{
    public static async Task<RegisterRequest> RegisterUser(HttpClient client, string email = "test@example.com")
    {
        var request = new RegisterRequest(email, "SecurePass123!", "SecurePass123!");
        await client.PostAsJsonAsync("/api/auth/register", request);
        return request;
    }

    public static async Task<AuthResponse> LoginUser(HttpClient client, string email, string password)
    {
        var loginRequest = new LoginRequest(email, password);
        var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
        return await response.Content.ReadFromJsonAsync<AuthResponse>();
    }
}