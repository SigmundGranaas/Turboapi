using System.Reflection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Turbo_pg_data.db;
using Turboapi.Infrastructure.Persistence;
using Xunit;

namespace Turbo.Auth.Behaviour;

public sealed class AuthHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("auth")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly IContainer _nats = new ContainerBuilder()
        .WithImage("nats:2.10-alpine")
        .WithCommand("-js")
        .WithPortBinding(4222, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(4222))
        .Build();

    private WebApplicationFactory<Turbo.Host.Auth.AuthHostProgram>? _factory;

    public HttpClient CreateClient() => _factory!.CreateClient();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());

        var setup = new DatabaseSetupService(_postgres.GetConnectionString(), LocateAuthMigrations());
        await setup.InitializeDatabaseAsync();
        await setup.RunMigrationsAsync();

        var natsUrl = $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(4222)}";

        var hostBin = Path.GetDirectoryName(
            typeof(Turbo.Host.Auth.AuthHostProgram).Assembly.Location)!;

        _factory = new WebApplicationFactory<Turbo.Host.Auth.AuthHostProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseContentRoot(hostBin);
            builder.UseSetting("Nats:Url", natsUrl);
            builder.ConfigureServices((context, services) =>
            {
                var dbDescriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<AuthDbContext>));
                if (dbDescriptor is not null) services.Remove(dbDescriptor);

                services.AddDbContext<AuthDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
            });
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await Task.WhenAll(_nats.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
    }

    public Task PauseBrokerAsync() => _nats.PauseAsync();
    public Task UnpauseBrokerAsync() => _nats.UnpauseAsync();

    private static string LocateAuthMigrations()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Turboapi-auth", "db")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate Turboapi-auth/db from test assembly location");
        return Path.Combine(dir.FullName, "Turboapi-auth", "db");
    }
}
