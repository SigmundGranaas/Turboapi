using Turbo.Messaging;

namespace Turbo.Outbox;

/// <summary>
/// Append-only queue of events that a module wants to publish. The contract is
/// that <see cref="AppendAsync"/> participates in the caller's ambient
/// database transaction — so committing the aggregate change and committing
/// the event row happen together or not at all.
///
/// The <typeparamref name="TDbContext"/> type parameter exists so that in a
/// modulith deployment where three module outboxes share one process, each
/// module's handlers resolve their own outbox via DI rather than competing
/// for a single <c>IOutbox</c> registration. The <c>TDbContext</c> is a
/// marker — the contract does not depend on EF Core's API surface.
///
/// Modules (or their UnitOfWork) call this; an out-of-band dispatcher
/// hosted service is responsible for moving the rows to a transport.
/// </summary>
public interface IOutbox<TDbContext>
{
    Task AppendAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
