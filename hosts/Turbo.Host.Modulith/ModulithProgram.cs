namespace Turbo.Host.Modulith;

/// <summary>
/// Marker for <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>:
/// resolving against this type points the test host at the modulith
/// assembly's top-level <c>Program</c>, which would otherwise collide with
/// the per-service Programs (Turboapi-auth, Turboapi-activity, Turboapi-geo)
/// that the modulith references.
/// </summary>
public class ModulithProgram;
