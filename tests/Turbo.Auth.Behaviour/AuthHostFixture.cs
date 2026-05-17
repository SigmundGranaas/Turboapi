using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Turbo_pg_data.db;
using Turboapi.Infrastructure.Persistence;
using Xunit;
using KafkaSettings = Turboapi.Infrastructure.Messaging.KafkaSettings;

namespace Turbo.Auth.Behaviour;

public sealed class AuthHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("auth")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:6.2.10")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public HttpClient CreateClient() => _factory!.CreateClient();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        var setup = new DatabaseSetupService(_postgres.GetConnectionString(), LocateAuthMigrations());
        await setup.InitializeDatabaseAsync();
        await setup.RunMigrationsAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureServices((context, services) =>
            {
                var dbDescriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<AuthDbContext>));
                if (dbDescriptor is not null) services.Remove(dbDescriptor);

                services.AddDbContext<AuthDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));

                services.Configure<KafkaSettings>(o =>
                {
                    o.BootstrapServers = _kafka.GetBootstrapAddress();
                });
            });
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await Task.WhenAll(_kafka.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
    }

    public Task PauseBrokerAsync() => _kafka.PauseAsync();
    public Task UnpauseBrokerAsync() => _kafka.UnpauseAsync();

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
