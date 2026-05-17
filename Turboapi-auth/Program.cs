using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Turboapi;
using Turboapi.Presentation.Middleware;

var builder = WebApplication.CreateBuilder(args);
var webAppPolicy = "WebAppPolicy";

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: webAppPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:8080",
                "http://localhost:5173",
                "https://kart.sandring.no"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthModule(builder.Configuration);

var otel = builder.Services.AddOpenTelemetry();
otel.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName ?? "Turboapi-Auth", serviceVersion: "1.0.0"));
otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation(options => { options.RecordException = true; });
    tracing.AddHttpClientInstrumentation(options => { options.RecordException = true; });
    tracing.AddEntityFrameworkCoreInstrumentation(options => { options.SetDbStatementForText = true; });
    tracing.AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
    });
});
otel.WithMetrics(metrics =>
{
    metrics.AddAspNetCoreInstrumentation();
    metrics.AddHttpClientInstrumentation();
    metrics.AddPrometheusExporter();
    metrics.AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
    });
});
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
    });
});

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapPrometheusScrapingEndpoint();

app.UseRouting();
app.UseCors(webAppPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
