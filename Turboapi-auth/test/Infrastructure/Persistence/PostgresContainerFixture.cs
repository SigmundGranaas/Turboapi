// File: /home/sigmund/development/turboapi/Turboapi/Turboapi-auth/test/Infrastructure.Tests/Persistence/PostgresContainerFixture.cs
using System;
using System.Diagnostics; // For Process
using System.IO;         // For Path
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;
using Turboapi.Infrastructure.Persistence;

namespace Turboapi.Infrastructure.Tests.Persistence
{
    public class PostgresContainerFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgreSqlContainer;
        public string ConnectionString => _postgreSqlContainer.GetConnectionString();
        private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        private readonly string _projectRootPath; // To locate Flyway migrations

        public PostgresContainerFixture()
        {
            _postgreSqlContainer = new PostgreSqlBuilder()
                .WithImage("postgres:15-alpine")
                .WithDatabase("test_auth_db")
                .WithUsername("testuser")
                .WithPassword("testpassword")
                // Expose the port for Flyway CLI if it runs outside the container network
                // This might not be strictly necessary if Flyway can connect to the mapped port.
                // .WithPortBinding(5432, true) 
                .Build();

            // Determine project root to find db/migrations
            // This logic might need adjustment based on your test execution environment
            var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (currentDir != null && !Directory.Exists(Path.Combine(currentDir.FullName, "db", "migrations")))
            {
                currentDir = currentDir.Parent;
                if (currentDir == null || currentDir.FullName == currentDir.Root.FullName) // Avoid infinite loop up to root
                {
                     throw new DirectoryNotFoundException("Could not find the 'db/migrations' directory. Ensure your test execution path allows traversal to the project root.");
                }
            }
            _projectRootPath = currentDir.FullName;
        }

        public AuthDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AuthDbContext>()
                .UseNpgsql(ConnectionString)
                .UseLoggerFactory(_loggerFactory)
                .EnableSensitiveDataLogging()
                .Options;
            return new AuthDbContext(options);
        }

        private async Task RunFlywayMigrationsAsync()
        {
            // Construct the JDBC URL for Flyway
            // The GetConnectionString() from Testcontainers is ADO.NET format.
            // We need to parse it or reconstruct it for JDBC.
            // Example: jdbc:postgresql://hostname:port/database
            var host = _postgreSqlContainer.Hostname;
            // GetMappedPublicPort might be needed if Flyway runs from host machine and not within docker network
            var port = _postgreSqlContainer.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort); 
            var database = "test_auth_db"; // As defined in container setup
            var user = "testuser";
            var password = "testpassword";

            var jdbcUrl = $"jdbc:postgresql://{host}:{port}/{database}";
            var migrationsLocation = $"filesystem:{Path.Combine(_projectRootPath, "db", "migrations")}";

            var flywayArgs = $"migrate -url=\"{jdbcUrl}\" -user=\"{user}\" -password=\"{password}\" -locations=\"{migrationsLocation}\" -baselineOnMigrate=true -cleanDisabled=false";
            
            // Output arguments for debugging
            Console.WriteLine($"Flyway arguments: {flywayArgs}");
            Console.WriteLine($"Attempting to run Flyway from: {_projectRootPath}");


            var processStartInfo = new ProcessStartInfo
            {
                FileName = "flyway", // Assumes flyway CLI is in PATH or use full path
                Arguments = flywayArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _projectRootPath // Set working directory if flyway.conf is there or for relative paths
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Flyway process.");
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Console.WriteLine("Flyway Output:\n" + output);
            if (process.ExitCode != 0)
            {
                Console.WriteLine("Flyway Error:\n" + error);
                throw new Exception($"Flyway migration failed with exit code {process.ExitCode}.\nOutput: {output}\nError: {error}");
            }
        }
        
        public async Task ResetDatabaseAsync()
        {
            // This method will clean the database and re-apply migrations for a fresh state
            // Useful if you want to run this before each test or test class
            using var context = CreateContext();
            // Option 1: Clean (if not disabled and safe for test DB) then migrate
            // await RunFlywayCommandAsync("clean"); // Be CAREFUL with clean on non-test DBs
            
            // Option 2: Drop all objects and re-migrate (Safer for test isolation)
            // This usually involves dropping the public schema or specific tables.
            // For simplicity, we'll rely on Flyway's ability to migrate an empty DB.
            // If your tests need a *truly* clean slate between test classes (not just individual tests),
            // you might consider dropping and recreating the DB or schema, or using different DB per test class.
            // For now, re-running migrations should be sufficient if they are idempotent or handle existing state.

            // A common approach is to ensure the database is empty before migrations for tests.
            // This can be complex to do generically.
            // For now, we'll assume Flyway handles migrating an already migrated (but possibly dirty) DB correctly,
            // or that data cleanup happens within the tests themselves.
            // The most robust way is often to drop/recreate the schema or DB.
            // Alternatively, run `flyway clean` (if enabled and safe) then `flyway migrate`.

            // Simple approach: let Flyway handle it. It will apply pending migrations.
            // If you need a pristine state for *each test method*, this fixture is not enough.
            // You'd need a transaction per test or more aggressive cleanup.
            // This fixture gives a pristine state *per test run* or *per collection*.
            await RunFlywayMigrationsAsync();
        }


        public async Task InitializeAsync()
        {
            await _postgreSqlContainer.StartAsync();
            await RunFlywayMigrationsAsync(); 
            // DO NOT use context.Database.EnsureDeletedAsync() or EnsureCreatedAsync() here
            // as Flyway is now managing the schema.
        }

        public async Task DisposeAsync()
        {
            await _postgreSqlContainer.StopAsync();
            await _postgreSqlContainer.DisposeAsync();
        }
    }

    [CollectionDefinition("PostgresContainerCollection")]
    public class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>
    {
    }
}