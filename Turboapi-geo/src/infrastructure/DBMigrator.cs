using System.Diagnostics;
using Npgsql;

namespace Turboapi_geo.infrastructure;

public class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly string _migrationsPath;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        string connectionString, 
        string migrationsPath,
        ILogger<DatabaseInitializer> logger)
    {
        _connectionString = connectionString;
        _migrationsPath = migrationsPath;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await CreateDatabaseIfNotExists();
        await RunFlywayMigrations();
    }

    private async Task CreateDatabaseIfNotExists()
    {
        try
        {
            // Parse connection string to get database name
            var builder = new NpgsqlConnectionStringBuilder(_connectionString);
            string databaseName = builder.Database;
            
            // Remove database name for connecting to default database
            builder.Database = "";
            
            using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            // Check if database exists
            using var cmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @dbname",
                conn);
            cmd.Parameters.AddWithValue("@dbname", databaseName);
            var exists = await cmd.ExecuteScalarAsync();

            if (exists == null)
            {
                _logger.LogInformation($"Creating database {databaseName}");
                using var createCmd = new NpgsqlCommand(
                    $"CREATE DATABASE {databaseName}", 
                    conn);
                await createCmd.ExecuteNonQueryAsync();
                
                // Enable PostGIS
                using var postgisConn = new NpgsqlConnection(_connectionString);
                await postgisConn.OpenAsync();
                using var postgisCmd = new NpgsqlCommand(
                    "CREATE EXTENSION IF NOT EXISTS postgis;", 
                    postgisConn);
                await postgisCmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating database");
            throw;
        }
    }

    private async Task RunFlywayMigrations()
    {
        try
        {
            // Convert standard connection string to JDBC format
            var builder = new NpgsqlConnectionStringBuilder(_connectionString);
            var jdbcUrl = $"jdbc:postgresql://{builder.Host}:{builder.Port}/{builder.Database}";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "flyway",
                    Arguments = $"migrate " +
                                $"-url=\"{jdbcUrl}\" " +
                                $"-user={builder.Username} " +
                                $"-password={builder.Password} " +
                                $"-locations=filesystem:{_migrationsPath} " +
                                "-baselineOnMigrate=true",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _logger.LogInformation("Running Flyway migrations...");
            process.Start();
        
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Flyway migration failed: {error}");
            }
        
            _logger.LogInformation("Flyway migrations completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running Flyway migrations");
            throw;
        }
    }
}