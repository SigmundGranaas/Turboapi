using System.Reflection;
using Npgsql;
using Turbo_pg_data.db;

namespace Turbo.Behaviour.Testing;

/// <summary>
/// Locates module artefacts (migration directories, content roots)
/// relative to the test assembly. Fixtures use this rather than
/// hardcoding paths.
/// </summary>
public static class RepoLayout
{
    /// <summary>
    /// Walks parents from the test assembly's bin directory until it
    /// finds a directory containing <paramref name="moduleDirectory"/>
    /// (e.g. <c>Turboapi-activity</c>) at the repo root, then joins the
    /// subsequent <paramref name="segments"/> onto it.
    /// </summary>
    public static string LocateModulePath(string moduleDirectory, params string[] segments)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, moduleDirectory)))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                $"Could not locate {moduleDirectory} starting from the test assembly directory.");

        var parts = new[] { dir.FullName, moduleDirectory }.Concat(segments).ToArray();
        return Path.Combine(parts);
    }

    /// <summary>
    /// Returns the bin/Debug/netNN.0 directory of the assembly that
    /// declares <typeparamref name="THostMarker"/>. WebApplicationFactory's
    /// content-root resolution doesn't reliably find the host's
    /// appsettings.Test.json when the marker lives in a different
    /// assembly from the test; passing this to UseContentRoot fixes that.
    /// </summary>
    public static string HostContentRoot<THostMarker>()
        => Path.GetDirectoryName(typeof(THostMarker).Assembly.Location)!;

    /// <summary>
    /// Creates a Postgres database (no-op if it already exists) and
    /// runs Flyway migrations against it from
    /// <paramref name="moduleDirectory"/>/db.
    /// </summary>
    public static async Task RunMigrationsAsync(string connectionString, string moduleDirectory)
    {
        var migrationsRoot = LocateModulePath(moduleDirectory, "db");
        var setup = new DatabaseSetupService(connectionString, migrationsRoot);
        await setup.RunMigrationsAsync();
    }

    /// <summary>
    /// Creates a named Postgres database against the given base
    /// connection string. Useful when one container hosts multiple
    /// per-module databases (the modulith and microservice topology
    /// fixtures both use this pattern).
    /// </summary>
    public static async Task CreateDatabaseAsync(string baseConnectionString, string databaseName)
    {
        await using var conn = new NpgsqlConnection(baseConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Returns the input connection string with the <c>Database</c>
    /// parameter replaced by <paramref name="databaseName"/>.
    /// </summary>
    public static string WithDatabase(string baseConnectionString, string databaseName)
    {
        var b = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = databaseName };
        return b.ConnectionString;
    }
}
