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
using Turboapi.auth;
using Turboapi.core;
using TurboApi.Data.Entity;
using Turboapi.infrastructure;
using Turboapi.services;
using Turboapi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();

builder.Services.Configure<GoogleAuthSettings>(
    builder.Configuration.GetSection("Authentication:Google"));
builder.Services.AddScoped<GoogleTokenValidator>();

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
    }).AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
    });


builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Register auth providers
builder.Services.AddScoped<IAuthenticationProvider, GoogleAuthenticationProvider>();
builder.Services.AddScoped<IAuthenticationProvider, PasswordAuthenticationProvider>();
builder.Services.AddScoped<IAuthenticationProvider, RefreshTokenProvider>();
builder.Services.AddKafkaIntegration(builder.Configuration);

// Configure HttpClient for Google auth
builder.Services.AddHttpClient<GoogleTokenValidator>();

var otel = builder.Services.AddOpenTelemetry();

// Configure OpenTelemetry Resources with the application name
otel.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName));

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


// Add Metrics for ASP.NET Core and our custom metrics and export to Prometheus
otel.WithMetrics(metrics => metrics
    // Metrics provider from OpenTelemetry
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    // Metrics provides by ASP.NET Core in .NET 8
    .AddMeter("Microsoft.AspNetCore.Hosting")
    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
    .AddOtlpExporter(otlpOptions =>
    {
        // Change this to point to your collector instead of Jaeger
        otlpOptions.Endpoint = new Uri("http://localhost:4317");
        otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
    })
    .AddPrometheusExporter());

// Export to Jaeger
otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddEntityFrameworkCoreInstrumentation();
    tracing.SetSampler(new AlwaysOnSampler());
    tracing.AddOtlpExporter(otlpOptions =>
    {
        // Change this to point to your collector instead of Jaeger
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

// For testing
public partial class Program { }

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

public static class EntityFrameworkCoreConfiguration
{
     static string GetStatementType(string sql)
    {
        sql = sql.TrimStart().ToUpper();
        if (sql.StartsWith("SELECT")) return "SELECT";
        if (sql.StartsWith("INSERT")) return "INSERT";
        if (sql.StartsWith("UPDATE")) return "UPDATE";
        if (sql.StartsWith("DELETE")) return "DELETE";
        return "OTHER";
    }

     static string GetAffectedTables(string sql)
    {
        // Simple parsing - you might want to use a proper SQL parser in production
        var fromIndex = sql.ToUpper().IndexOf("FROM ");
        if (fromIndex == -1) return "";
    
        var tableSection = sql.Substring(fromIndex + 5);
        var endIndex = tableSection.IndexOfAny(new[] { ' ', '\n', '\r' });
    
        return endIndex == -1 ? tableSection : tableSection.Substring(0, endIndex);
    }
}

