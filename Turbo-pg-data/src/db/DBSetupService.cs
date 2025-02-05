
using Npgsql;
using Turbo_pg_data.db;
using Turbo_pg_data.flyway;

public class DatabaseSetupService : IDatabaseSetupService
{
    private readonly string _connectionString;
    private readonly string _migrationsPath;
    private readonly FlywayMigrationRunner _flywayRunner;

    public DatabaseSetupService(string connectionString, string migrationsPath)
    {
        _connectionString = connectionString;
        _migrationsPath = migrationsPath;
        _flywayRunner = new FlywayMigrationRunner(connectionString, migrationsPath);
    }

    public string GetConnectionString() => _connectionString;

    public async Task InitializeDatabaseAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var database = builder.Database;
        builder.Database = "postgres";

        using var conn = new NpgsqlConnection(builder.ToString());
        await conn.OpenAsync();

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{database}'";
        var exists = await checkCmd.ExecuteScalarAsync();

        if (exists == null)
        {
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"CREATE DATABASE {database}";
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task RunMigrationsAsync()
    {
        await _flywayRunner.RunMigrationsAsync();
    }
}