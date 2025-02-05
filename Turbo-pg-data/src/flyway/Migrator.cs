using System.Diagnostics;
using Npgsql;

namespace Turbo_pg_data.flyway;
public class FlywayMigrationRunner
{
    private readonly string _connectionString;
    private readonly string _migrationsPath;

    public FlywayMigrationRunner(string connectionString, string migrationsPath)
    {
        _connectionString = connectionString;
        _migrationsPath = migrationsPath;
    }

    public async Task RunMigrationsAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var jdbcUrl = $"jdbc:postgresql://{builder.Host}:{builder.Port}/{builder.Database}";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "flyway",
                Arguments = $"migrate -url=\"{jdbcUrl}\" -user=\"{builder.Username}\" -password=\"{builder.Password}\" -locations=filesystem:{_migrationsPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
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
}