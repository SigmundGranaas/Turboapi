using Turbo.Messaging;

namespace Turbo.Outbox;

/// <summary>
/// Append-only queue of events that a module wants to publish. The contract is
/// that <see cref="AppendAsync"/> participates in the caller's ambient
/// database transaction — so committing the aggregate change and committing
/// the event row happen together or not at all.
///
/// Modules (or their UnitOfWork) call this; an out-of-band dispatcher
/// hosted service is responsible for moving the rows to a transport.
/// </summary>
public interface IOutbox
{
    Task AppendAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
