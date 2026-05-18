using Turbo.Messaging.Nats;
using Turboapi.Geo;
using TurboAuthentication.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAuthorization();

builder.Services.AddTurboAuth(builder.Configuration);
builder.Services.AddGeoModule(builder.Configuration);

builder.Services.AddNatsMessaging(o =>
{
    o.Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222";
    o.StreamName = "TURBO_GEO";
    o.Subjects = ["turbo.geo.>"];
    o.SubjectPrefix = "turbo.geo";
});
builder.Services.AddGeoNatsSubscribers();

var app = builder.Build();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

namespace Turbo.Host.Geo
{
    /// <summary>Marker for WebApplicationFactory in tests.</summary>
    public class GeoHostProgram;
}
