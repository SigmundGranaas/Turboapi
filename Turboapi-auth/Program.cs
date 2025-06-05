using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Turboapi.Application.Interfaces; 
using Turboapi.Domain.Interfaces;
using Turboapi.Infrastructure.Auth;      
using Turboapi.Infrastructure.Messaging; 
using Turboapi.Infrastructure.Persistence;
using Turboapi.Infrastructure.Persistence.Repositories;
using Turboapi.Infrastructure.Auth.OAuthProviders; 

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
builder.Configuration.GetSection("Database").Bind(dbOptionsConfig); 

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
builder.Services.AddScoped<IAuthTokenService, JwtService>();
builder.Services.Configure<JwtConfig>(builder.Configuration.GetSection("Jwt"));

// OAuth Provider Configuration
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration.GetSection("Authentication:Google"));
// Register HttpClient for GoogleOAuthAdapter. 
// You can configure primary message handlers, policies (retry, Polly) here if needed.
builder.Services.AddHttpClient(nameof(GoogleOAuthAdapter)); // Named client based on class name
// Register the adapter. If you have multiple OAuth providers, you might use a factory or named registrations.
builder.Services.AddScoped<IOAuthProviderAdapter, GoogleOAuthAdapter>(); // Example for Google


// #############################################
// # 4. Integration Services
// #############################################
builder.Services.AddHttpClient(); // Add default IHttpClientFactory
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
    .AddRuntimeInstrumentation() 
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
    tracing.AddHttpClientInstrumentation(options => // This will instrument HttpClient calls made by GoogleOAuthAdapter
    {
        options.RecordException = true;
    });
    tracing.AddEntityFrameworkCoreInstrumentation(options =>
    {
        options.SetDbStatementForText = true;
    });
    tracing.AddSource("Turboapi.Infrastructure.Messaging.KafkaEventPublisher"); 
    
    tracing.SetSampler(new AlwaysOnSampler()); 
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
    app.MapOpenApi(); 
    app.MapScalarApiReference(); 
}

app.MapPrometheusScrapingEndpoint();


app.UseCors(policy => policy 
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.UseHttpsRedirection();
app.UseAuthentication(); 
app.UseAuthorization();  
app.MapControllers();

app.Run();

// For testing
public partial class Program { }

public class DatabaseOptions 
{
    public string Host { get; set; } = "localhost";
    public string Port { get; set; } = "5432";
    public string Database { get; set; } = "auth";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "yourpassword";
}