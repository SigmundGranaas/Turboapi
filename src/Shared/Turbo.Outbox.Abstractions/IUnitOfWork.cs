namespace Turbo.Outbox;

/// <summary>
/// Commit boundary for a module. Handlers run their stage-changes work
/// inside <see cref="SaveChangesAsync"/>; the implementation owns the
/// underlying transaction and retry semantics (Postgres' execution
/// strategy, in our case).
///
/// The <typeparamref name="TScope"/> type parameter is an opaque module
/// marker so that in a modulith with three modules each handler resolves
/// its own unit of work via DI. The marker carries no methods and no
/// infrastructure dependencies — handlers can reference it without
/// pulling EF Core, Npgsql, or any storage-specific type into the
/// application layer.
/// </summary>
public interface IUnitOfWork<TScope>
{
    Task SaveChangesAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);
}
