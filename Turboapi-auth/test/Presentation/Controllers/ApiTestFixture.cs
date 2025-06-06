using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Turboapi.Application.Interfaces;
using Turboapi.Infrastructure.Persistence;
using Xunit;

namespace Turboapi.Presentation.Tests.Controllers
{
    public class ApiTestFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _dbContainer;
        private readonly string _projectRootPath;
        public HttpClient Client { get; private set; } = null!;
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public TestEventPublisher EventPublisher { get; } = new();

        public ApiTestFixture()
        {
            _dbContainer = new PostgreSqlBuilder()
                .WithImage("postgres:15-alpine")
                .WithDatabase("e2e_test_db")
                .WithUsername("testuser")
                .WithPassword("testpassword")
                .Build();

            var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (currentDir != null && !Directory.Exists(Path.Combine(currentDir.FullName, "db", "migrations")))
            {
                currentDir = currentDir.Parent;
                if (currentDir == null || currentDir.FullName == currentDir.Root.FullName)
                {
                     throw new DirectoryNotFoundException("Could not find the 'db/migrations' directory.");
                }
            }
            _projectRootPath = currentDir.FullName;
        }

        public async Task InitializeAsync()
        {
            await _dbContainer.StartAsync();

            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    // Let the factory use its default Kestrel server setup, which includes HTTPS
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<DbContextOptions<AuthDbContext>>();
                        services.AddDbContext<AuthDbContext>(options =>
                            options.UseNpgsql(_dbContainer.GetConnectionString()));
                        
                        services.RemoveAll<IEventPublisher>();
                        services.AddSingleton<IEventPublisher>(EventPublisher);
                    });
                });
                
            // By default, CreateClient targets the HTTPS endpoint if it's available.
            // This is the simplest way to ensure our tests run over a secure connection.
            Client = Factory.CreateClient();
            await ResetDatabaseAsync(); 
        }
        
        // ... The rest of the ApiTestFixture (ResetDatabaseAsync, etc.) remains unchanged ...

        public async Task ResetDatabaseAsync()
        {
            await RunFlywayCommandAsync("clean", cleanDisabled: false);
            await RunFlywayCommandAsync("migrate");
        }
        
        private async Task RunFlywayCommandAsync(string command, bool cleanDisabled = true)
        {
            var host = _dbContainer.Hostname;
            var port = _dbContainer.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort); 
            var database = "e2e_test_db";
            var user = "testuser";
            var password = "testpassword";
            var jdbcUrl = $"jdbc:postgresql://{host}:{port}/{database}";
            var migrationsLocation = $"filesystem:{Path.Combine(_projectRootPath, "db", "migrations")}";
            var flywayArgs = $"{command} -url=\"{jdbcUrl}\" -user=\"{user}\" -password=\"{password}\" -locations=\"{migrationsLocation}\" -baselineOnMigrate=true -cleanDisabled={cleanDisabled.ToString().ToLower()}";
            
            var processStartInfo = new ProcessStartInfo("flyway", flywayArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _projectRootPath
            };

            using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Failed to start Flyway process.");
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Flyway command '{command}' failed. Error: {error}");
            }
        }

        public async Task DisposeAsync()
        {
            await _dbContainer.DisposeAsync();
            await Factory.DisposeAsync();
        }
    }

    [CollectionDefinition("ApiCollection")]
    public class ApiCollection : ICollectionFixture<ApiTestFixture> {}
    
    public class TestEventPublisher : IEventPublisher
    {
        public List<object> PublishedEvents { get; } = new();
        public Task PublishAsync<TEvent>(TEvent domainEvent) where TEvent : Domain.Events.IDomainEvent
        {
            PublishedEvents.Add(domainEvent);
            return Task.CompletedTask;
        }
        public void Clear() => PublishedEvents.Clear();
    }
}