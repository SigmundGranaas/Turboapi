using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Turbo.Messaging;
using Turbo.Messaging.Nats;
using Turboauth_activity.data;
using Turboauth_activity.domain.events;
using Turboauth_activity.domain.handler;
using Turboauth_activity.domain.query;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Configuration.AddEnvironmentVariables();

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
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
            ValidIssuer = "turbo-auth",
            ValidateAudience = false,
        };
    });


var dbOptions = new DatabaseOptions
{
    Host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
    Port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5436",
    Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "activity",
    Username = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
    Password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "yourpassword"
};

var connectionString = $"Host={dbOptions.Host};Port={dbOptions.Port};Database={dbOptions.Database};Username={dbOptions.Username};Password={dbOptions.Password}";

// Register DbContext
builder.Services.AddDbContext<ActivityContext>((s, options) =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure();
    });
});

builder.Services.AddScoped<CreateActivityHandler>();
builder.Services.AddScoped<EditActivityHandler>();
builder.Services.AddScoped<DeleteActivityHandler>();

builder.Services.AddScoped<IActivityReadRepository, ActivityReadRepository>();
builder.Services.AddScoped<IActivityWriteRepository, ActivityWriteRepository>();

builder.Services.AddScoped<IEventHandler<ActivityCreated>, ActivityEventHandler>();
builder.Services.AddScoped<IEventHandler<ActivityUpdated>, ActivityEventHandler>();
builder.Services.AddScoped<IEventHandler<ActivityDeleted>, ActivityEventHandler>();

// Outbox-driven publish path: command handlers append to the outbox in the
// same DB transaction as the aggregate change. A hosted dispatcher polls
// the outbox and publishes envelopes through IMessageTransport. The
// transport is NATS JetStream; subscribers receive via the JetStream
// consumer host registered below.
builder.Services.AddScoped<Turbo.Outbox.IOutbox, Turbo.Outbox.Postgres.PgOutbox<ActivityContext>>();
builder.Services.AddHostedService<Turbo.Outbox.Postgres.OutboxDispatcherHostedService<ActivityContext>>();

builder.Services.AddNatsMessaging(o =>
{
    o.Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222";
    o.StreamName = "TURBO_ACTIVITY";
    o.Subjects = ["turbo.activity.>"];
    o.SubjectPrefix = "turbo.activity";
});

builder.Services.AddNatsSubscriber<ActivityCreated>(
    "turbo.activity.ActivityCreated", "activity-created");
builder.Services.AddNatsSubscriber<ActivityUpdated>(
    "turbo.activity.ActivityUpdated", "activity-updated");
builder.Services.AddNatsSubscriber<ActivityDeleted>(
    "turbo.activity.ActivityDeleted", "activity-deleted");

builder.Services.AddScoped<ActivityQueryHandler>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
// app.MapPrometheusScrapingEndpoint();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public class DatabaseOptions
{
    public string Host { get; set; }
    public string Port { get; set; }
    public string Database { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}

public partial class Program { }