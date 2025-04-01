using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

// Service-specific usings
using Turboapi.auth;
using Turboapi.controller;
using Turboapi.core;
using TurboApi.Data.Entity;
using Turboapi.infrastructure;
using Turboapi.services;

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
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// #############################################
// # 3. Authentication Configuration
// #############################################
// 3.1 Password and Token Services
builder.Services.AddHttpClient();
builder.Services.AddScoped<AuthHelper>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IGoogleAuthenticationService, GoogleAuthenticationService>();

builder.Services.AddDataProtection();

// 3.2 JWT Configuration
var jwtConfig = builder.Configuration.GetSection("Jwt").Get<JwtConfig>();
builder.Services.Configure<JwtConfig>(builder.Configuration.GetSection("Jwt"));

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
                Encoding.UTF8.GetBytes(jwtConfig.Key)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = jwtConfig.Issuer,
            ValidAudience = jwtConfig.Audience,
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "Default",
        policy  =>
        {
            policy
                .WithOrigins("http://localhost:8080")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
});

// 3.3 Authentication Providers
builder.Services.AddScoped<IAuthenticationProvider, GoogleAuthenticationProvider>();
builder.Services.AddScoped<IAuthenticationProvider, PasswordAuthenticationProvider>();
builder.Services.AddScoped<IAuthenticationProvider, RefreshTokenProvider>();

// 3.4 Google Authentication
builder.Services.Configure<GoogleAuthSettings>(
    builder.Configuration.GetSection("Authentication:Google"));


// #############################################
// # 4. Integration Services
// #############################################
// 4.1 Kafka Configuration
builder.Services.AddKafkaIntegration(builder.Configuration);

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
app.UseMiddleware<ExceptionLoggingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.UseCors("Default");
app.Run();

// For testing
public partial class Program { }

// #############################################
// # 7. Extension Methods
// #############################################
public static class KafkaConfiguration
{
    public static IServiceCollection AddKafkaIntegration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KafkaSettings>(
            configuration.GetSection("Kafka"));
        
        services.AddSingleton<IEventPublisher>(sp => 
            new KafkaEventPublisher(
                sp.GetRequiredService<IOptions<KafkaSettings>>(),
                sp.GetRequiredService<ILogger<KafkaEventPublisher>>()
            ));        
        return services;
    }
}