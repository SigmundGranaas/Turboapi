using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
using AuthenticationService = Turboapi.services.AuthenticationService;
using IAuthenticationService = Turboapi.services.IAuthenticationService;

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

// Register DbContext
builder.Services.AddDbContext<AuthDbContext>((s, options) =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure();
    });
});

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

builder.Services.Configure<CookieSettings>(options => {
    // Bind from appsettings.json first
    builder.Configuration.GetSection("Cookie").Bind(options);
    
    // Override with environment variables
    options.Domain = Environment.GetEnvironmentVariable("COOKIE_DOMAIN") ?? options.Domain;
    options.SameSite = Environment.GetEnvironmentVariable("COOKIE_SAME_SITE") ?? options.SameSite;
    options.Secure = CookieSettings.ParseBoolEnvVar("COOKIE_SECURE", options.Secure);
    options.ExpiryDays = CookieSettings.ParseIntEnvVar("COOKIE_EXPIRY_DAYS", options.ExpiryDays);
    options.Path = Environment.GetEnvironmentVariable("COOKIE_PATH") ?? options.Path;
    options.UseAdditionalEncryption = CookieSettings.ParseBoolEnvVar("COOKIE_USE_ADDITIONAL_ENCRYPTION", options.UseAdditionalEncryption);
});

// 3.2 JWT Configuration
var jwtConfig = builder.Configuration.GetSection("Jwt").Get<JwtConfig>();
builder.Services.Configure<JwtConfig>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddAuthentication("AuthScheme")
    .AddPolicyScheme("AuthScheme", "Authorization Bearer or Cookie", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            // Check if the request contains the JWT bearer token
            string authorization = context.Request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer "))
                return JwtBearerDefaults.AuthenticationScheme;
            
            // Otherwise use cookies
            return CookieAuthenticationDefaults.AuthenticationScheme;
        };
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        
        // Prevent redirects for API calls
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                // Return 401 instead of redirect for API calls
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
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
    });


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

public class DatabaseOptions
{
    public string Host { get; set; }
    public string Port { get; set; }
    public string Database { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}