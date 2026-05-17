using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Turbo.Messaging.Nats;
using Turboauth_activity;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Activity validates JWTs that Auth issues. In a microservice deployment
// Activity does not need the cookie scheme, only JwtBearer.
builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(opt =>
{
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        ValidIssuer = "turbo-auth",
        ValidateAudience = false,
    };
});

builder.Services.AddActivityModule(builder.Configuration);

builder.Services.AddNatsMessaging(o =>
{
    o.Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222";
    o.StreamName = "TURBO_ACTIVITY";
    o.Subjects = ["turbo.activity.>"];
    o.SubjectPrefix = "turbo.activity";
});
builder.Services.AddActivityNatsSubscribers();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
