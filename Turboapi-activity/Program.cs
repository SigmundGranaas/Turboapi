using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Turbo_event.kafka;
using Turbo_event.test.kafka;
using Turboapi.Infrastructure.Kafka;
using Turboauth_activity.data;
using Turboauth_activity.domain.events;
using Turboauth_activity.domain.handler;
using Turboauth_activity.domain.query;
using KafkaSettings = Turbo_event.kafka.KafkaSettings;

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
    Port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432",
    Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "geo",
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

// Kafka
builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection("Kafka"));

// Register the topic initializer
builder.Services.AddSingleton<ITopicInitializer, SimpleKafkaTopicInitializer>();

// Register event infrastructure
builder.Services.AddSingleton<IEventTopicResolver, ActivityEventTopicResolver>();
builder.Services.AddSingleton<IEventStoreWriter, KafkaEventStoreWriter>();
builder.Services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();

builder.Services.AddKafkaConsumer<ActivityCreated, ActivityEventHandler>("activities", "activity-created");
builder.Services.AddKafkaConsumer<ActivityUpdated, ActivityEventHandler>("activities", "activity-updated");
builder.Services.AddKafkaConsumer<ActivityDeleted, ActivityEventHandler>("activities", "activity-deleted");

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