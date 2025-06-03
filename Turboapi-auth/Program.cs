// Program.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Turboapi.Application.Interfaces; // For IEventPublisher
using Turboapi.Domain.Interfaces;
using Turboapi.Infrastructure.Auth;      // For PasswordHasher
using Turboapi.Infrastructure.Messaging; // For KafkaEventPublisher, KafkaSettings
using Turboapi.Infrastructure.Persistence;
using Turboapi.Infrastructure.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

// #############################################
// # 1. Core API Configuration
// #############################################
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Configuration.AddEnvironmentVariables();

// #############################################
// # 2. Database Configuration
// #############################################
var dbOptionsConfig = new DatabaseOptions();
builder.Configuration.GetSection("Database").Bind(dbOptionsConfig); // Allow config from appsettings

var connectionString = $"Host={Environment.GetEnvironmentVariable("DB_HOST") ?? dbOptionsConfig.Host};" +
                       $"Port={Environment.GetEnvironmentVariable("DB_PORT") ?? dbOptionsConfig.Port};" +
                       $"Database={Environment.GetEnvironmentVariable("DB_NAME") ?? dbOptionsConfig.Database};" +
                       $"Username={Environment.GetEnvironmentVariable("DB_USER") ?? dbOptionsConfig.Username};" +
                       $"Password={Environment.GetEnvironmentVariable("DB_PASSWORD") ?? dbOptionsConfig.Password}";

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        npgsqlOptions.EnableRetryOnFailure()));

// Register Repositories
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

// #############################################
// # 3. Authentication Configuration
// #############################################
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
// Other auth services will be added in later phases (IAuthTokenService, etc.)

// #############################################
// # 4. Integration Services
// #############################################
// 4.1 Kafka Configuration
builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();


// #############################################
// # 5. Observability Configuration
// #############################################
var otel = builder.Services.AddOpenTelemetry();

otel.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName ?? "Turboapi-Auth"));

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(
            ResourceBuilder.CreateDefault().AddService("Turboapi-Auth"))
        .AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
        });
});

otel.WithMetrics(metrics => metrics
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddRuntimeInstrumentation() // .NET runtime metrics
    .AddMeter("Microsoft.AspNetCore.Hosting")
    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
    .AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
        otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
    })
    .AddPrometheusExporter());

otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation(options =>
    {
        options.RecordException = true;
    });
    tracing.AddHttpClientInstrumentation(options =>
    {
        options.RecordException = true;
    });
    tracing.AddEntityFrameworkCoreInstrumentation(options =>
    {
        options.SetDbStatementForText = true;
    });
    // Add more instrumentations as needed, e.g., for Kafka when official support is better
    // For now, KafkaEventPublisher has manual ActivitySource instrumentation.
    tracing.AddSource("Turboapi.Infrastructure.Messaging.KafkaEventPublisher"); // ActivitySource name
    
    tracing.SetSampler(new AlwaysOnSampler()); // Consider ParentBasedSampler with TraceIdRatioBasedSampler for prod
    tracing.AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
        otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
    });
});

// #############################################
// # 6. App Configuration and Middleware
// #############################################
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // Or app.UseSwagger(); app.UseSwaggerUI();
    app.MapScalarApiReference(); // Or other API exploration tool
}

app.MapPrometheusScrapingEndpoint();

// app.UseMiddleware<GlobalExceptionMiddleware>(); // To be added in Presentation Layer phase

app.UseCors(policy => policy // More specific CORS policy recommended for prod
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.UseHttpsRedirection();
app.UseAuthentication(); // To be configured in later phases
app.UseAuthorization();  // To be configured in later phases
app.MapControllers();

app.Run();

// For testing
public partial class Program { }

public class DatabaseOptions // Moved from being nested in Program.cs for better accessibility if needed
{
    public string Host { get; set; } = "localhost";
    public string Port { get; set; } = "5432";
    public string Database { get; set; } = "auth";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "yourpassword";
}