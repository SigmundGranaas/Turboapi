using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Turboapi_geo.data;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;
using TurboAuthentication.Extensions;

var builder = WebApplication.CreateBuilder(args);

// #############################################
// # 1. Core API Configuration
// #############################################
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Configuration.AddEnvironmentVariables();

// #############################################
// # 1.1 Security
// #############################################

builder.Services.AddTurboAuth(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "Default",
        policy  =>
        {
            policy
                .WithOrigins("http://localhost:8080", "https://kart-api.sandring.no",  "https://kart.sandring.no")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
});

// #############################################
// # 2. Database Configuration
// #############################################
var dbOptions = new DatabaseOptions
{
    Host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
    Port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5435",
    Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "geo",
    Username = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
    Password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "yourpassword"
};

var connectionString = $"Host={dbOptions.Host};Port={dbOptions.Port};Database={dbOptions.Database};Username={dbOptions.Username};Password={dbOptions.Password}";

// Register DbContext
builder.Services.AddDbContext<LocationReadContext>((s, options) =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure();
        npgsqlOptions.UseNetTopologySuite();
    });
});

builder.Services.AddScoped<ILocationWriteRepository, EfLocationWriteRepository>();
builder.Services.AddScoped<ILocationReadRepository, EfLocationWriteRepository.EfLocationReadRepository>();

builder.Services.AddScoped<IDirectReadModelProjector, DirectReadModelProjector>();

builder.Services.AddScoped<LocationCreatedHandler>();
builder.Services.AddScoped<LocationUpdatedHandler>();
builder.Services.AddScoped<LocationDeletedHandler>();

builder.Services.AddScoped<GetLocationByIdHandler>();
builder.Services.AddScoped<GetLocationsInExtentHandler>();
builder.Services.AddScoped<CreateLocationHandler>();
builder.Services.AddScoped<DeleteLocationHandler>();
builder.Services.AddScoped<UpdateLocationHandler>();
builder.Services.AddScoped<GeometryFactory>();

// #############################################
// # Integration Services
// #############################################
// 4.1 Kafka Configuration — kept until step 5 replaces the transport with NATS.
builder.Services.AddKafkaEventInfrastructure(builder.Configuration);

// 4.2 Outbox-driven publish path
// Command handlers append to the outbox in the same DB transaction as the
// aggregate change and the synchronous read-model projection. A hosted
// dispatcher polls the outbox and publishes envelopes through
// IMessageTransport; the temporary KafkaMessageTransport forwards them to
// the existing location-events topic so any future downstream consumer can
// pick them up.
builder.Services.AddScoped<Turbo.Outbox.IOutbox, Turbo.Outbox.Postgres.PgOutbox<LocationReadContext>>();
builder.Services.AddSingleton<Func<Turbo.Messaging.EventEnvelope, string>>(_ => _ => "location-events");
builder.Services.AddSingleton<Turbo.Messaging.IMessageTransport, Turboapi_geo.infrastructure.GeoKafkaMessageTransport>();
builder.Services.AddHostedService<Turbo.Outbox.Postgres.OutboxDispatcherHostedService<LocationReadContext>>();

// #############################################
// # Observability Configuration
// #############################################
var otel = builder.Services.AddOpenTelemetry();

// 5.1 Configure OpenTelemetry Resources
otel.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName));

// 5.2 Logging Configuration
builder.Logging.AddOpenTelemetry(options =>
{
    options
        .SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService("TurboApi-geo"))
        .AddOtlpExporter(opt =>
        {
            opt.Endpoint = new Uri("http://localhost:4317");
        });
});

// 5.3 Metrics Configuration
otel.WithMetrics(metrics => metrics
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddMeter("Microsoft.AspNetCore.Hosting")
    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
    .AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri("http://localhost:4317");
        otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
    })
    .AddPrometheusExporter());

// 5.4 Tracing Configuration
otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddEntityFrameworkCoreInstrumentation();
    tracing.SetSampler(new AlwaysOnSampler());
    tracing.AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri("http://localhost:4317");
        otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
    });
});

var app = builder.Build();


app.MapOpenApi();
app.MapScalarApiReference();
app.MapPrometheusScrapingEndpoint();
app.UseMiddleware<ExceptionLoggingMiddleware>();
app.UseCors("Default");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public class DatabaseOptions
{
    public string Host { get; set; }
    public string Port { get; set; }
    public string Database { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}

public partial class Program { }
