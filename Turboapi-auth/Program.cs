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
using Turboapi.Application.Behaviors;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.AuthenticateWithOAuth;
using Turboapi.Application.UseCases.Commands.LoginUserWithPassword;
using Turboapi.Application.UseCases.Commands.RefreshToken;
using Turboapi.Application.UseCases.Commands.RegisterUserWithPassword;
using Turboapi.Application.UseCases.Commands.RevokeRefreshToken;
using Turboapi.Application.UseCases.Queries.ValidateSession;
using Turboapi.Domain.Interfaces;
using Turboapi.Infrastructure.Auth;
using Turboapi.Infrastructure.Auth.OAuthProviders;
using Turboapi.Infrastructure.Messaging;
using Turboapi.Infrastructure.Persistence;
using Turboapi.Infrastructure.Persistence.Repositories;
using Turboapi.Presentation.Cookies;
using Turboapi.Presentation.Middleware;
using Turboapi.Presentation.Security;

var builder = WebApplication.CreateBuilder(args);
var webAppPolicy = "WebAppPolicy";

// #############################################
// # 1. Core Services Configuration
// #############################################
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// #############################################
// # 1.5 CORS Configuration for Web Client
// #############################################
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: webAppPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000", // Common React/Flutter dev port
                "http://localhost:8080",
                "http://localhost:5173", // Common Vite dev port
                "https://kart.sandring.no" // Production frontend
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // Essential for cookie-based auth
    });
});


// #############################################
// # 2. Database & Persistence Configuration
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

// Register Repositories and Unit of Work
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// #############################################
// # 3. Application Use Case Handlers
// #############################################

// Register Command Handlers with the UnitOfWork decorator
builder.Services.AddCommandHandler<RegisterUserWithPasswordCommand, Result<AuthTokenResponse, RegistrationError>, RegisterUserWithPasswordCommandHandler>();
builder.Services.AddCommandHandler<LoginUserWithPasswordCommand, Result<AuthTokenResponse, LoginError>, LoginUserWithPasswordCommandHandler>();
builder.Services.AddCommandHandler<RefreshTokenCommand, Result<AuthTokenResponse, RefreshTokenError>, RefreshTokenCommandHandler>();
builder.Services.AddCommandHandler<AuthenticateWithOAuthCommand, Result<AuthTokenResponse, OAuthLoginError>, AuthenticateWithOAuthCommandHandler>();
builder.Services.AddCommandHandler<RevokeRefreshTokenCommand, Result<RefreshTokenError>, RevokeRefreshTokenCommandHandler>();

// Register Query Handlers (no decorator needed)
builder.Services.AddScoped<ValidateSessionQueryHandler>();

// #############################################
// # 3.5 Authentication & Authorization
// #############################################
var jwtConfig = builder.Configuration.GetSection("Jwt").Get<JwtConfig>()!;
builder.Services.AddSingleton(jwtConfig);

// Register the custom ticket format handler. It's stateless so Singleton is fine.
builder.Services.AddSingleton<ISecureDataFormat<AuthenticationTicket>, JwtDataFormat>();

// Configure authentication to use Cookies as the default, but also support JWT Bearer tokens.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = CookieManager.AccessTokenCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax; // Lax is fine as OAuth flow is a top-level navigation
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        options.TicketDataFormat = builder.Services.BuildServiceProvider().GetRequiredService<ISecureDataFormat<AuthenticationTicket>>();
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.Key)),
            ValidateIssuer = true,
            ValidIssuer = jwtConfig.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtConfig.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();


// #############################################
// # 4. Infrastructure & Integration Services
// #############################################
builder.Services.AddHttpClient();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IAuthTokenService, JwtService>();
builder.Services.Configure<JwtConfig>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<CookieSettings>(builder.Configuration.GetSection("Cookie"));
builder.Services.AddScoped<Turboapi.Presentation.Cookies.ICookieManager, CookieManager>();
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration.GetSection("Authentication:Google"));
builder.Services.AddHttpClient<GoogleOAuthAdapter>();
builder.Services.AddScoped<IOAuthProviderAdapter, GoogleOAuthAdapter>();
builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();

// #############################################
// # 5. Observability (OpenTelemetry)
// #############################################
var otel = builder.Services.AddOpenTelemetry();
otel.ConfigureResource(resource => resource
    .AddService(serviceName: builder.Environment.ApplicationName ?? "Turboapi-Auth", serviceVersion: "1.0.0"));
otel.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation(options => { options.RecordException = true; });
    tracing.AddHttpClientInstrumentation(options => { options.RecordException = true; });
    tracing.AddEntityFrameworkCoreInstrumentation(options => { options.SetDbStatementForText = true; });
    tracing.AddSource(new System.Diagnostics.ActivitySource("Turboapi.Infrastructure.Messaging.*").Name);
    tracing.AddOtlpExporter(otlpOptions =>
    {
        otlpOptions.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTLP_ENDPOINT_URL") ?? "http://localhost:4317");
    });
});
otel.WithMetrics(metrics =>
{
    metrics.AddAspNetCoreInstrumentation();
    metrics.AddHttpClientInstrumentation();
    metrics.AddRuntimeInstrumentation();
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

// #############################################
// # 6. Middleware Pipeline Configuration
// #############################################
var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapPrometheusScrapingEndpoint();

app.UseRouting(); // Routing must come before CORS and Auth

app.UseCors(webAppPolicy); // Apply the CORS policy

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

// #############################################
// # 7. Local Classes and Extension Methods
// #############################################
public partial class Program { }
public class DatabaseOptions
{
    public string Host { get; set; } = "localhost";
    public string Port { get; set; } = "5432";
    public string Database { get; set; } = "auth";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "yourpassword";
}
public static class CommandHandlerServiceCollectionExtensions
{
    public static IServiceCollection AddCommandHandler<TCommand, TResponse, THandler>(this IServiceCollection services)
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(provider =>
            new UnitOfWorkCommandHandlerDecorator<TCommand, TResponse>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<IUnitOfWork>())
        );
        return services;
    }
}