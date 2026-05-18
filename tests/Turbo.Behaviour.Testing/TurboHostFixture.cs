using System.Net.Http.Headers;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Turbo.Behaviour.Testing;

/// <summary>
/// Common Postgres + NATS + WebApplicationFactory wiring for a single-module
/// host. Subclass per host (Auth/Activity/Geo) and override
/// <see cref="ModuleDirectory"/> + <see cref="ConfigureTestServices"/>.
/// </summary>
public abstract class TurboHostFixture<THost> : IAsyncLifetime where THost : class
{
    private readonly PostgreSqlContainer _postgres;
    private readonly IContainer _nats = TurboTestContainers.NatsJetStream();
    private WebApplicationFactory<THost>? _factory;
    private TurboJwtIssuer? _jwt;

    protected TurboHostFixture(string databaseName)
    {
        _postgres = TurboTestContainers.PostgresWithPostGis(databaseName);
    }

    /// <summary>Repo-relative directory whose db/migrations/ Flyway runs against.</summary>
    protected abstract string ModuleDirectory { get; }

    /// <summary>
    /// Hook for replacing the module's DbContext registration with one that
    /// points at the Testcontainers Postgres. Use <see cref="ReplaceDbContext"/>.
    /// </summary>
    protected abstract void ConfigureTestServices(WebHostBuilderContext context, IServiceCollection services);

    protected string ConnectionString => _postgres.GetConnectionString();

    public HttpClient CreateClient() => _factory!.CreateClient();

    public HttpClient CreateClientAs(Guid userId)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _jwt!.Issue(userId));
        return client;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _nats.StartAsync());
        await RepoLayout.RunMigrationsAsync(_postgres.GetConnectionString(), ModuleDirectory);

        _factory = new WebApplicationFactory<THost>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseContentRoot(RepoLayout.HostContentRoot<THost>());
            builder.UseSetting("Nats:Url", TurboTestContainers.NatsUrl(_nats));
            builder.ConfigureServices((context, services) =>
            {
                _jwt = new TurboJwtIssuer(context.Configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("Jwt:Key not configured for Test environment"));
                ConfigureTestServices(context, services);
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

    /// <summary>
    /// Swap a DbContext's options registration for one bound to this fixture's
    /// Testcontainers Postgres. Use from <see cref="ConfigureTestServices"/>.
    /// </summary>
    protected static void ReplaceDbContext<TContext>(
        IServiceCollection services,
        Action<DbContextOptionsBuilder> configure) where TContext : DbContext
    {
        var dbDescriptor = services.SingleOrDefault(d =>
            d.ServiceType == typeof(DbContextOptions<TContext>));
        if (dbDescriptor is not null) services.Remove(dbDescriptor);
        services.AddDbContext<TContext>(configure);
    }
}
