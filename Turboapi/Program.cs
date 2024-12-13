using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Turboapi.auth;
using Turboapi.core;
using TurboApi.Data.Entity;
using Turboapi.Models;
using Turboapi.services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
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
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();

// Register auth providers
builder.Services.AddScoped<IAuthenticationProvider, GoogleAuthenticationProvider>();

// Configure HttpClient for Google auth
builder.Services.AddHttpClient<GoogleTokenValidator>();


var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();


// Run Flyway migrations before starting the app
var startInfo = new ProcessStartInfo
{
    FileName = "flyway",
    Arguments = "migrate",
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false
};

using (var process = Process.Start(startInfo))
{
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new Exception("Flyway migration failed");
    }
}


app.Run();

// For testing
public partial class Program { }