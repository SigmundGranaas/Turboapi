using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

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
var dbOptions = new DatabaseOptions
{
    Host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
    Port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432",
    Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "auth",
    Username = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
    Password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "yourpassword"
};

var connectionString = $"Host={dbOptions.Host};Port={dbOptions.Port};Database={dbOptions.Database};Username={dbOptions.Username};Password={dbOptions.Password}";

// #############################################
// # 3. Authentication Configuration
// #############################################
// 3.1 Password and Token Services
builder.Services.AddHttpClient();




// #############################################
// # 4. Integration Services
// #############################################
// 4.1 Kafka Configuration

// #############################################
// # 5. Observability Configuration
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
                .AddService("TurboApi-auth"))
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

// #############################################
// # 6. App Configuration and Middleware
// #############################################
var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapPrometheusScrapingEndpoint();
app.UseCors("Default");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

// For testing
public partial class Program { }

public class DatabaseOptions
{
    public string Host { get; set; }
    public string Port { get; set; }
    public string Database { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}