using Turbo.Host.Modulith;
using Turbo.Messaging.InProcess;
using Turboapi.Auth;
using Turboapi.Geo;
using Turboapi.Activity;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

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
// Subscriber registrations live in SubscriberWiring so the
// SubscriberCoverage architecture test can compare them against the
// set of IDomainEvent types in the modules.
builder.Services.AddTurboInProcessSubscribers();

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
