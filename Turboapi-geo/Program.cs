using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NetTopologySuite.Geometries;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Turboapi_geo.domain.events;
using Turboapi_geo.domain.handler;
using Turboapi_geo.domain.query;
using Turboapi_geo.domain.query.model;
using Turboapi_geo.infrastructure;
using Turboapi.infrastructure;

var builder = WebApplication.CreateBuilder(args);

// #############################################
// # 1. Core API Configuration
// #############################################
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();


// #############################################
// # 1.1 Security
// #############################################
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
            ValidIssuer = "turbo-auth",
            ValidateAudience = false,
        };
    });


// #############################################
// # 2. Database Configuration
// #############################################
builder.Services.AddDbContext<LocationReadContext>((s, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseNetTopologySuite();
        npgsqlOptions.EnableRetryOnFailure();
    });
});

builder.Services.AddScoped<ILocationWriteRepository, EfLocationWriteRepository.EfLocationWriteModelRepository>();
builder.Services.AddScoped<ILocationReadModelRepository, EfLocationWriteRepository.EfLocationReadModelRepository>();

builder.Services.AddScoped<LocationEventHandler>();
builder.Services.AddScoped<GetLocationByIdHandler>();
builder.Services.AddScoped<GetLocationsInExtentHandler>();
builder.Services.AddScoped<CreateLocationHandler>();
builder.Services.AddScoped<DeleteLocationHandler>();
builder.Services.AddScoped<UpdateLocationPositionHandler>();
builder.Services.AddScoped<GeometryFactory>();

// #############################################
// # Integration Services
// #############################################
// 4.1 Kafka Configuration
// Register handlers
builder.Services.AddScoped<ILocationEventHandler<LocationCreated>, LocationCreatedHandler>();
builder.Services.AddScoped<ILocationEventHandler<LocationPositionChanged>, LocationPositionChangedHandler>();
builder.Services.AddScoped<ILocationEventHandler<LocationDeleted>, LocationDeletedHandler>();

builder.Services.AddKafkaEventInfrastructure(builder.Configuration);

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
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
