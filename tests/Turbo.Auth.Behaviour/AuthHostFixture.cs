using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Turbo.Behaviour.Testing;
using Turboapi.Infrastructure.Persistence;
using Xunit;

namespace Turbo.Auth.Behaviour;

public sealed class AuthHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = TurboTestContainers.PostgresWithPostGis("auth");
    private readonly IContainer _nats = TurboTestContainers.NatsJetStream();

    private WebApplicationFactory<Turbo.Host.Auth.AuthHostProgram>? _factory;

    public HttpClient CreateClient() => _factory!.CreateClient();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());
        await RepoLayout.RunMigrationsAsync(_postgres.GetConnectionString(), "Turboapi-auth");

        _factory = new WebApplicationFactory<Turbo.Host.Auth.AuthHostProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseContentRoot(RepoLayout.HostContentRoot<Turbo.Host.Auth.AuthHostProgram>());
            builder.UseSetting("Nats:Url", TurboTestContainers.NatsUrl(_nats));
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
}
