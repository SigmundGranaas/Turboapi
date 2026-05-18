using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Turbo_pg_data.db;
using Turbo.Host.Modulith;
using Xunit;

namespace Turbo.Modulith.Behaviour;

/// <summary>
/// Boots <c>Turbo.Host.Modulith</c> on one Postgres container with three
/// separate databases (auth/activity/geo) — the modulith deploy topology.
/// No NATS; the in-process transport delivers events.
/// </summary>
public sealed class ModulithHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:17-3.5-alpine")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<ModulithProgram>? _factory;

    public HttpClient CreateClient() => _factory!.CreateClient();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var baseConnString = _postgres.GetConnectionString();
        await CreateDatabaseAsync(baseConnString, "auth");
        await CreateDatabaseAsync(baseConnString, "activity");
        await CreateDatabaseAsync(baseConnString, "geo");

        var authConn = WithDatabase(baseConnString, "auth");
        var activityConn = WithDatabase(baseConnString, "activity");
        var geoConn = WithDatabase(baseConnString, "geo");

        await RunMigrationsAsync(authConn, "Turboapi-auth");
        await RunMigrationsAsync(activityConn, "Turboapi-activity");
        await RunMigrationsAsync(geoConn, "Turboapi-geo");

        _factory = new WebApplicationFactory<ModulithProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:Auth", authConn);
            builder.UseSetting("ConnectionStrings:Activity", activityConn);
            builder.UseSetting("ConnectionStrings:Geo", geoConn);

            // No ConfigureServices override: the module extensions read the
            // connection strings via configuration.GetConnectionString(...),
            // and UseSetting above feeds them in. Re-registering the
            // DbContexts here would duplicate the providers and confuse the
            // outbox dispatcher (it would resolve a different DbContext than
            // the one the command handler wrote through).
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Resolves the running modulith host's in-process bus so a test can
    /// simulate at-least-once redelivery — publishing the same envelope
    /// twice to assert the idempotency table dedupes.
    /// </summary>
    public Turbo.Messaging.InProcess.InProcessMessageBus Bus
        => _factory!.Services.GetRequiredService<Turbo.Messaging.InProcess.InProcessMessageBus>();

    /// <summary>
    /// Reads the most-recent activity-outbox row whose event type ends
    /// with <paramref name="eventTypeSuffix"/> and republishes it on the
    /// in-process bus as if the broker had redelivered. Tests use this
    /// to exercise the idempotency dedup path without synthesising an
    /// envelope (which would defeat the purpose — dedup is keyed on
    /// EventId, so a fresh id would always be "new").
    /// </summary>
    public async Task RedeliverLatestActivityEnvelopeAsync(string eventTypeSuffix)
    {
        var baseConn = _postgres.GetConnectionString();
        var activityConn = WithDatabase(baseConn, "activity");
        await using var conn = new NpgsqlConnection(activityConn);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            @"SELECT id, event_type, source, data_content_type, payload_json, headers_json, occurred_at
              FROM activity.outbox
              WHERE event_type LIKE @suffix
              ORDER BY position DESC
              LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("@suffix", "%" + eventTypeSuffix);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException(
                $"No outbox row found matching event_type LIKE %{eventTypeSuffix}");

        var id = reader.GetGuid(0);
        var type = reader.GetString(1);
        var source = reader.GetString(2);
        var contentType = reader.GetString(3);
        var payload = reader.GetString(4);
        var headersJson = reader.GetString(5);
        var occurredAt = reader.GetDateTime(6);

        var headers = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(headersJson)
            ?? new Dictionary<string, string>();

        var envelope = new Turbo.Messaging.EventEnvelope(
            EventId: id,
            Type: type,
            Source: source,
            Time: occurredAt,
            DataContentType: contentType,
            Data: System.Text.Encoding.UTF8.GetBytes(payload),
            Headers: headers);

        await reader.CloseAsync();
        await Bus.PublishAsync(envelope, CancellationToken.None);
    }

    /// <summary>
    /// Counts rows in the activity read model. Used by the idempotency
    /// test as an authoritative check that a redelivery did not insert
    /// a duplicate row — the HTTP collection endpoint would only show
    /// rows owned by a single caller and we want the global count.
    /// </summary>
    public async Task<int> CountActivityRowsAsync(Guid activityId)
    {
        var baseConn = _postgres.GetConnectionString();
        var activityConn = WithDatabase(baseConn, "activity");
        await using var conn = new NpgsqlConnection(activityConn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM activity_query WHERE activity_id = @id;", conn);
        cmd.Parameters.AddWithValue("@id", activityId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Test-only simulation of the operator rebuild SOP: truncate the
    /// activity read model + the activity dedup table, then mark every
    /// activity outbox row as undispatched. The outbox dispatcher will
    /// re-publish from position 0 the next time it polls; the in-process
    /// subscriber re-projects each event into the freshly-empty read
    /// model. Asserts the outbox is sufficient to rebuild the read model
    /// from zero — i.e. event sourcing actually works.
    /// </summary>
    public async Task ResetActivityReadModelForReplayAsync()
    {
        var baseConn = _postgres.GetConnectionString();
        var activityConn = WithDatabase(baseConn, "activity");
        await using var conn = new NpgsqlConnection(activityConn);
        await conn.OpenAsync();

        await using var truncateReadModel = new NpgsqlCommand("TRUNCATE TABLE activity_query;", conn);
        await truncateReadModel.ExecuteNonQueryAsync();

        await using var truncateDedup = new NpgsqlCommand("TRUNCATE TABLE activity.processed_events;", conn);
        await truncateDedup.ExecuteNonQueryAsync();

        await using var resetOutbox = new NpgsqlCommand(
            "UPDATE activity.outbox SET dispatched_at = NULL, attempts = 0, last_error = NULL;", conn);
        await resetOutbox.ExecuteNonQueryAsync();
    }

    private static async Task CreateDatabaseAsync(string baseConnString, string dbName)
    {
        await using var conn = new NpgsqlConnection(baseConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\";", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RunMigrationsAsync(string connectionString, string moduleDirectory)
    {
        var migrationsRoot = LocateRepoPath(moduleDirectory, "db");
        var setup = new DatabaseSetupService(connectionString, migrationsRoot);
        await setup.RunMigrationsAsync();
    }

    private static string LocateRepoPath(params string[] segments)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, segments[0])))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException($"Could not locate {segments[0]} from test assembly location");
        return Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
    }

    private static string WithDatabase(string baseConnString, string databaseName)
    {
        var b = new NpgsqlConnectionStringBuilder(baseConnString) { Database = databaseName };
        return b.ConnectionString;
    }

}

[CollectionDefinition("ModulithHost")]
public sealed class ModulithHostCollection : ICollectionFixture<ModulithHostFixture> { }
