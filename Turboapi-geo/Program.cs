using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Turbo.Messaging.Nats;
using Turboapi.infrastructure;
using Turboapi_geo;
using TurboAuthentication.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAuthorization();

builder.Services.AddTurboAuth(builder.Configuration);

builder.Services.AddGeoModule(builder.Configuration);

builder.Services.AddNatsMessaging(o =>
{
    o.Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222";
    o.StreamName = "TURBO_GEO";
    o.Subjects = ["turbo.geo.>"];
    o.SubjectPrefix = "turbo.geo";
});

var otel = builder.Services.AddOpenTelemetry();
otel.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName));
otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddEntityFrameworkCoreInstrumentation(options => { options.SetDbStatementForText = true; });
    tracing.AddOtlpExporter(otlp =>
    {
        otlp.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
    });
});
otel.WithMetrics(metrics =>
{
    metrics.AddAspNetCoreInstrumentation();
    metrics.AddHttpClientInstrumentation();
    metrics.AddOtlpExporter(otlp =>
    {
        otlp.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
    });
});
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter(otlp =>
    {
        otlp.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
    });
});

var app = builder.Build();
app.UseMiddleware<ExceptionLoggingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
