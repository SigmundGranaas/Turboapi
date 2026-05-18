using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Turbo.Behaviour.Testing;
using ActivityContext = Turboapi.Activity.data.ActivityContext;

namespace Turbo.Activity.Behaviour;

public sealed class ActivityHostFixture : TurboHostFixture<Turbo.Host.Activity.ActivityHostProgram>
{
    public ActivityHostFixture() : base("activity") { }

    protected override string ModuleDirectory => "Turboapi-activity";

    protected override void ConfigureTestServices(WebHostBuilderContext context, IServiceCollection services)
        => ReplaceDbContext<ActivityContext>(services, o => o.UseNpgsql(ConnectionString));
}
