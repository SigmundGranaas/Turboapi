using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Turbo.Architecture;

/// <summary>
/// Closes review gap #5: the three modules must not have direct
/// references to one another. If Activity ever depended on Geo (or
/// vice versa) the "messages only" contract would silently degrade
/// to "messages plus a sneaky compile-time call." The shared
/// abstraction packages (Turbo.Messaging.*, Turbo.Outbox.*,
/// Turbo.Messaging.Nats / InProcess) are the only legal way for one
/// module to reach another.
/// </summary>
public sealed class ModuleBoundaries
{
    private static readonly string[] ModuleAssemblyNames =
    {
        "Turboapi-activity",
        "Turboapi-geo",
        "Turboapi-auth",
    };

    private static Assembly Activity => typeof(Turboapi.Activity.ActivityScope).Assembly;
    private static Assembly Geo => typeof(Turboapi_geo.GeoScope).Assembly;
    private static Assembly Auth => typeof(Turboapi.AuthScope).Assembly;

    [Fact]
    public void Activity_does_not_reference_Geo_or_Auth_assemblies()
    {
        AssertNoCrossModuleReference(Activity, forbidden: ["Turboapi-geo", "Turboapi-auth"]);
    }

    [Fact]
    public void Geo_does_not_reference_Activity_or_Auth_assemblies()
    {
        AssertNoCrossModuleReference(Geo, forbidden: ["Turboapi-activity", "Turboapi-auth"]);
    }

    [Fact]
    public void Auth_does_not_reference_Activity_or_Geo_assemblies()
    {
        AssertNoCrossModuleReference(Auth, forbidden: ["Turboapi-activity", "Turboapi-geo"]);
    }

    private static void AssertNoCrossModuleReference(Assembly module, string[] forbidden)
    {
        var referenced = module.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .ToHashSet()!;

        var violations = forbidden.Where(referenced.Contains!).ToList();

        violations.Should().BeEmpty(
            $"{module.GetName().Name} must communicate with other modules only through events on the shared messaging abstractions; "
            + $"direct assembly reference defeats the modulith/microservice symmetry. Forbidden references found: {string.Join(", ", violations)}");
    }
}
