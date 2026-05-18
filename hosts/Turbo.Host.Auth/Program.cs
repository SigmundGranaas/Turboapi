using Turboapi.Auth;
using Turboapi.Auth.Presentation.Middleware;

var builder = WebApplication.CreateBuilder(args);
var webAppPolicy = "WebAppPolicy";

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: webAppPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:8080",
                "http://localhost:5173",
                "https://kart.sandring.no"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthModule(builder.Configuration);

var app = builder.Build();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseRouting();
app.UseCors(webAppPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();
app.Run();

namespace Turbo.Host.Auth
{
    /// <summary>Marker for WebApplicationFactory in tests.</summary>
    public class AuthHostProgram;
}
