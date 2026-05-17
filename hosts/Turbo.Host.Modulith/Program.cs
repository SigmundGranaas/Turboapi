using Turbo.Messaging.InProcess;
using Turboapi;
using Turboapi_geo;
using Turboauth_activity;
using Turboauth_activity.domain.events;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// All three modules in one process. AuthModule owns the Cookie+JwtBearer
// scheme; Activity and Geo controllers use it as the default authentication
// scheme (their [Authorize] attributes don't pin a specific scheme name).
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddActivityModule(builder.Configuration);
builder.Services.AddGeoModule(builder.Configuration);

// In-process transport: outbox dispatchers publish here, the subscriber host
// drains the channel and resolves IEventHandler<T> in a fresh DI scope. No
// NATS, no broker — the read-model projection is end-to-end in-process.
builder.Services.AddInProcessMessaging();
builder.Services.AddInProcessSubscriber<ActivityCreated>("turbo.activity.ActivityCreated");
builder.Services.AddInProcessSubscriber<ActivityUpdated>("turbo.activity.ActivityUpdated");
builder.Services.AddInProcessSubscriber<ActivityDeleted>("turbo.activity.ActivityDeleted");

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
