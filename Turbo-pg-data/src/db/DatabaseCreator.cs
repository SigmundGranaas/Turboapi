using Npgsql;

namespace Turbo_pg_data.db;

public class DatabaseCreator
{
    private readonly string _connectionString;

    public DatabaseCreator(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task CreateDatabaseAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var database = builder.Database;
        builder.Database = "postgres";

        using var conn = new NpgsqlConnection(builder.ToString());
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {database} WITH (FORCE); CREATE DATABASE {database};";
        await cmd.ExecuteNonQueryAsync();
    }
}