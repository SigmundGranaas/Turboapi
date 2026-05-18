using System.Net.Http.Headers;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Turbo.Behaviour.Testing;
using Turboapi_geo.domain.query.model;
using Xunit;

namespace Turbo.Geo.Behaviour;

public sealed class GeoHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = TurboTestContainers.PostgresWithPostGis("geo");
    private readonly IContainer _nats = TurboTestContainers.NatsJetStream();

    private WebApplicationFactory<Turbo.Host.Geo.GeoHostProgram>? _factory;
    private TurboJwtIssuer? _jwt;

    public HttpClient CreateClient() => _factory!.CreateClient();

    public HttpClient CreateClientAs(Guid userId)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwt!.Issue(userId));
        return client;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());
        await RepoLayout.RunMigrationsAsync(_postgres.GetConnectionString(), "Turboapi-geo");

        _factory = new WebApplicationFactory<Turbo.Host.Geo.GeoHostProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseContentRoot(RepoLayout.HostContentRoot<Turbo.Host.Geo.GeoHostProgram>());
            builder.UseSetting("Nats:Url", TurboTestContainers.NatsUrl(_nats));
            builder.ConfigureServices((context, services) =>
            {
                _jwt = new TurboJwtIssuer(context.Configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("Jwt:Key not configured for Test environment"));

                var dbDescriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<LocationReadContext>));
                if (dbDescriptor is not null) services.Remove(dbDescriptor);

                services.AddDbContext<LocationReadContext>(o =>
                    o.UseNpgsql(_postgres.GetConnectionString(), x => x.UseNetTopologySuite()));
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
