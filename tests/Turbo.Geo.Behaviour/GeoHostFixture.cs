using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Turbo_pg_data.db;
using Turboapi.infrastructure;
using Turboapi_geo.domain.query.model;
using Xunit;

namespace Turbo.Geo.Behaviour;

public sealed class GeoHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:17-3.5-alpine")
        .WithDatabase("geo")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:6.2.10")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private string _jwtSecret = string.Empty;

    public HttpClient CreateClient() => _factory!.CreateClient();

    public HttpClient CreateClientAs(Guid userId)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueJwt(userId));
        return client;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        var setup = new DatabaseSetupService(_postgres.GetConnectionString(), LocateGeoMigrations());
        await setup.InitializeDatabaseAsync();
        await setup.RunMigrationsAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureServices((context, services) =>
            {
                _jwtSecret = context.Configuration["Jwt:Key"]
                             ?? throw new InvalidOperationException("Jwt:Key not configured for Test environment");

                var dbDescriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<LocationReadContext>));
                if (dbDescriptor is not null) services.Remove(dbDescriptor);

                services.AddDbContext<LocationReadContext>(o =>
                    o.UseNpgsql(_postgres.GetConnectionString(), x => x.UseNetTopologySuite()));

                services.Configure<KafkaSettings>(o =>
                {
                    o.BootstrapServers = _kafka.GetBootstrapAddress();
                    o.LocationEventsTopic = "location-events";
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

    private static string LocateGeoMigrations()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Turboapi-geo", "db")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate Turboapi-geo/db from test assembly location");
        return Path.Combine(dir.FullName, "Turboapi-geo", "db");
    }

    private string IssueJwt(Guid userId)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "turbo-auth",
            Audience = "turbo-client",
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
