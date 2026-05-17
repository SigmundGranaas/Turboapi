using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Turboapi_geo.controller.request;
using Turboapi_geo.controller.response;
using Xunit;

namespace Turbo.Geo.Behaviour;

/// <summary>
/// Failure-mode guarantee: when the broker is unreachable at the moment a
/// write commits, the write still succeeds with 201 and a subsequent read
/// returns the body. Today Geo also projects synchronously through
/// DirectReadModelProjector inside the same DB transaction as the outbox
/// write, so the read is available immediately rather than after the
/// dispatcher catches up — both behaviours are user-visible and acceptable
/// expressions of the same guarantee.
/// </summary>
[Collection("GeoHost")]
public sealed class GeoDurabilityBehaviour
{
    private readonly GeoHostFixture _host;
    public GeoDurabilityBehaviour(GeoHostFixture host) => _host = host;

    [Fact]
    public async Task creates_succeed_and_become_visible_even_while_the_broker_is_down()
    {
        var owner = Guid.NewGuid();
        var client = _host.CreateClientAs(owner);
        Guid locationId;

        await _host.PauseBrokerAsync();
        try
        {
            var request = new CreateLocationRequest
            {
                Geometry = new GeometryData { Longitude = -0.1, Latitude = 51.5 },
                Display = new DisplayData { Name = "Recorded during outage", Description = "", Icon = "pin" }
            };
            var create = await client.PostAsJsonAsync("/api/geo/Locations", request);
            create.StatusCode.Should().Be(HttpStatusCode.Created,
                "the write path must commit to the outbox even when the broker is unreachable");
            locationId = (await create.Content.ReadFromJsonAsync<LocationResponse>())!.Id;

            var get = await client.GetAsync($"/api/geo/Locations/{locationId}");
            get.IsSuccessStatusCode.Should().BeTrue(
                "the synchronous read-model projection runs in the same transaction as the outbox write");
            var body = await get.Content.ReadFromJsonAsync<LocationResponse>();
            body!.Display.Name.Should().Be("Recorded during outage");
        }
        finally
        {
            await _host.UnpauseBrokerAsync();
        }

        // After recovery the same endpoint still answers — confirming the outage
        // did not corrupt or roll back the committed state.
        var afterRecovery = await client.GetAsync($"/api/geo/Locations/{locationId}");
        afterRecovery.IsSuccessStatusCode.Should().BeTrue();
    }
}
