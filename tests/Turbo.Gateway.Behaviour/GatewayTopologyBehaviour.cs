using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Turboapi_gateway;
using Xunit;

namespace Turbo.Gateway.Behaviour;

/// <summary>
/// The gateway's routing surface is the deploy contract: changing topology
/// without keeping the routes aligned silently breaks the front door. These
/// tests probe each topology purely through HTTP — a route the gateway
/// recognises returns 502 (it tried to forward to an unreachable backend),
/// a route it does not recognise returns 404.
/// </summary>
public sealed class GatewayTopologyBehaviour
{
    private static WebApplicationFactory<GatewayProgram> BuildGateway(string topology) =>
        new WebApplicationFactory<GatewayProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Topology", topology);
        });

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/geo/locations")]
    [InlineData("/api/activity")]
    public async Task modulith_topology_routes_every_module_prefix(string path)
    {
        using var factory = BuildGateway("Modulith");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "the gateway must register a route for {0} when running as a modulith front", path);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/geo/locations")]
    [InlineData("/api/activity")]
    public async Task microservices_topology_routes_every_module_prefix(string path)
    {
        using var factory = BuildGateway("Microservices");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "the gateway must register a route for {0} when running as microservices", path);
    }

    [Fact]
    public async Task unknown_paths_return_404_under_either_topology()
    {
        foreach (var topology in new[] { "Modulith", "Microservices" })
        {
            using var factory = BuildGateway(topology);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/nonexistent/endpoint");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "the gateway must not invent routes — topology {0}", topology);
        }
    }
}
