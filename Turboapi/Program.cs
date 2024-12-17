using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Turboapi.auth;
using Turboapi.core;
using TurboApi.Data.Entity;
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

// Configure HttpClient for Google auth
builder.Services.AddHttpClient<GoogleTokenValidator>();


var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// For testing
public partial class Program { }