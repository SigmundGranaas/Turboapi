namespace Turboauth_activity;

/// <summary>
/// Opaque marker for the Activity module's commit boundary. Handlers depend on
/// <c>IOutbox&lt;IActivityScope&gt;</c> and <c>IUnitOfWork&lt;IActivityScope&gt;</c>; the
/// composition root binds those to the EF Core implementation against
/// <c>ActivityContext</c>. The marker is intentionally empty so the application
/// layer never references EF Core, Npgsql, or any storage-specific type.
/// </summary>
public interface IActivityScope;
