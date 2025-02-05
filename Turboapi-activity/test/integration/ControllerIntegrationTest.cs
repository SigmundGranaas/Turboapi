using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Turbo_event.kafka;
using Turbo_pg_data.db;
using Turbo_pg_data.flyway;
using Turboauth_activity.controller;
using Turboauth_activity.domain;
using Xunit;
using ActivityContext = Turboauth_activity.data.ActivityContext;

namespace Turboauth_activity.test.integration;

public class ControllerIntegrationTest: IAsyncLifetime
{
    private readonly KafkaContainer _kafka;
    private WebApplicationFactory<Program>? _factory;
    private readonly PostgreSqlContainer _postgres;
    private HttpClient? _client;
    private IDatabaseSetupService _databaseSetupService;
    private string? _jwtSecret;



    public ControllerIntegrationTest()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("turbo")
            .WithUsername("postgres")
            .WithPassword("your_password")
            .Build();
        
        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:6.2.10")
            .WithName("kafka-integration")
            .WithPortBinding(9092, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        // Start containers
        await _postgres.StartAsync();
        await _kafka.StartAsync();

        var migrationPath  = FindRootPath.findMigrationPathFromSource("db");
        _databaseSetupService = new DatabaseSetupService(_postgres.GetConnectionString(), migrationPath);
        await _databaseSetupService.InitializeDatabaseAsync();
        await _databaseSetupService.RunMigrationsAsync();
        
        // Build app
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
                        d.ServiceType == typeof(DbContextOptions<ActivityContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContext<ActivityContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString())
                    );

                    // Remove old Default setup
                    var descriptorsToRemove = services.Where(d =>
                        d.ServiceType == typeof(KafkaSettings));
            
                    foreach (var desc in descriptorsToRemove)
                    {
                        services.Remove(desc);
                    }
                    
                    // Configure Kafka settings
                    var kafkaSettings = new KafkaSettings
                    {
                        BootstrapServers = _kafka.GetBootstrapAddress(),
                        Topic = "activities"
                    };
        
                    services.Configure<KafkaSettings>(options =>
                    {
                        options.BootstrapServers = kafkaSettings.BootstrapServers;
                        options.Topic = kafkaSettings.Topic;
                    });
                });
            });
                
        _client = _factory.CreateClient();
    }
    
    public async Task DisposeAsync()
    {
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
    public async Task CreateAndGetActivity()
    {
        var guid = Guid.NewGuid();
        AddAuthHeader(guid.ToString());
        
        var createActivityRequest = new ActivityController.CreateActivityRequest(new Position { Latitude = 12, Longitude = 13}, "test", "test", "icon");
        var response = await _client.PostAsJsonAsync("/api/activity/", createActivityRequest);

        response.IsSuccessStatusCode.Should().BeTrue();
        var createdActivity = await response.Content.ReadFromJsonAsync<ActivityController.CreateActivityResponse>();
        
        var activity = await WaitForCondition(async () =>
            {
                try
                {
                    var getResponse = await _client.GetAsync("/api/activity/" + createdActivity.ActivityId);
                    getResponse.IsSuccessStatusCode.Should().BeTrue();
                    return await getResponse.Content.ReadFromJsonAsync<ActivityController.ActivityResponse>();
                }
                catch
                {
                    return null;
                }
            }, timeoutMessage: $"Activity {createdActivity.ActivityId} was not found");

        
        Assert.Equal(createdActivity.ActivityId, activity.Id);
    }
    
    private async Task<T?> WaitForCondition<T>(
        Func<Task<T?>> condition,
        TimeSpan? timeout = null,
        string? timeoutMessage = null)
    {
        timeout ??= TimeSpan.FromSeconds(1);
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout)
        {
            var conditionResult = await condition();
            if (conditionResult != null)
            {
                return conditionResult;
            }
            
            await Task.Delay(100); // Wait 100ms before next attempt
        }

        throw new TimeoutException(
            timeoutMessage ?? $"Condition not met after {timeout.Value.TotalSeconds} seconds");
    }
}