using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Turbo.Behaviour.Testing;
using Turboapi.Geo.domain.query.model;

namespace Turbo.Geo.Behaviour;

public sealed class GeoHostFixture : TurboHostFixture<Turbo.Host.Geo.GeoHostProgram>
{
    public GeoHostFixture() : base("geo") { }

    protected override string ModuleDirectory => "Turboapi-geo";

    protected override void ConfigureTestServices(WebHostBuilderContext context, IServiceCollection services)
        => ReplaceDbContext<LocationReadContext>(services,
            o => o.UseNpgsql(ConnectionString, x => x.UseNetTopologySuite()));
}
