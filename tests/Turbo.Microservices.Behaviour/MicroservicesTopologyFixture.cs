using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Turbo_pg_data.db;
using Xunit;

namespace Turbo.Microservices.Behaviour;

/// <summary>
/// Boots all three microservice host projects (Turbo.Host.Auth,
/// Turbo.Host.Activity, Turbo.Host.Geo) as separate
/// <see cref="WebApplicationFactory{TEntryPoint}"/> instances in the same
/// test process, sharing one Postgres container with three databases and
/// one NATS container. This matches the production microservice deploy:
/// three independent HTTP services that communicate through the broker
/// only, never directly in-process. Tests assert the cross-host contract
/// (JWT issued by Auth authorizes Activity/Geo, Activity projection
/// converges over NATS).
/// </summary>
public sealed class MicroservicesTopologyFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:17-3.5-alpine")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly IContainer _nats = new ContainerBuilder()
        .WithImage("nats:2.10-alpine")
        .WithCommand("-js")
        .WithPortBinding(4222, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4222))
        .Build();

    private WebApplicationFactory<Turbo.Host.Auth.AuthHostProgram>? _authFactory;
    private WebApplicationFactory<Turbo.Host.Activity.ActivityHostProgram>? _activityFactory;
    private WebApplicationFactory<Turbo.Host.Geo.GeoHostProgram>? _geoFactory;

    public HttpClient AuthClient => _authFactory!.CreateClient();
    public HttpClient ActivityClient => _activityFactory!.CreateClient();
    public HttpClient GeoClient => _geoFactory!.CreateClient();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());

        var baseConn = _postgres.GetConnectionString();
        await CreateDatabaseAsync(baseConn, "auth");
        await CreateDatabaseAsync(baseConn, "activity");
        await CreateDatabaseAsync(baseConn, "geo");

        var authConn = WithDatabase(baseConn, "auth");
        var activityConn = WithDatabase(baseConn, "activity");
        var geoConn = WithDatabase(baseConn, "geo");

        await RunMigrationsAsync(authConn, "Turboapi-auth");
        await RunMigrationsAsync(activityConn, "Turboapi-activity");
        await RunMigrationsAsync(geoConn, "Turboapi-geo");

        var natsUrl = $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(4222)}";

        _authFactory = BuildFactory<Turbo.Host.Auth.AuthHostProgram>(authConn, natsUrl, "Auth");
        _activityFactory = BuildFactory<Turbo.Host.Activity.ActivityHostProgram>(activityConn, natsUrl, "Activity");
        _geoFactory = BuildFactory<Turbo.Host.Geo.GeoHostProgram>(geoConn, natsUrl, "Geo");
    }

    public async Task DisposeAsync()
    {
        _authFactory?.Dispose();
        _activityFactory?.Dispose();
        _geoFactory?.Dispose();
        await Task.WhenAll(_nats.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
    }

    private static WebApplicationFactory<T> BuildFactory<T>(
        string connectionString, string natsUrl, string moduleConnStringName)
        where T : class
    {
        var hostBin = Path.GetDirectoryName(typeof(T).Assembly.Location)!;
        return new WebApplicationFactory<T>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseContentRoot(hostBin);
            builder.UseSetting("Nats:Url", natsUrl);
            builder.UseSetting($"ConnectionStrings:{moduleConnStringName}", connectionString);
        });
    }

    private static async Task CreateDatabaseAsync(string baseConn, string dbName)
    {
        await using var conn = new NpgsqlConnection(baseConn);
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

    private static string WithDatabase(string baseConn, string dbName)
    {
        var b = new NpgsqlConnectionStringBuilder(baseConn) { Database = dbName };
        return b.ConnectionString;
    }
}

[CollectionDefinition("MicroservicesTopology")]
public sealed class MicroservicesTopologyCollection : ICollectionFixture<MicroservicesTopologyFixture> { }
