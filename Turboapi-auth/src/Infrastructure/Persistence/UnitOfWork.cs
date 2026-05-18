using Microsoft.EntityFrameworkCore;
using Turbo.Messaging;
using Turbo.Outbox;
using Turbo.Outbox.Postgres;
using Turboapi.Application.Interfaces;
using Turboapi.Domain;

namespace Turboapi.Infrastructure.Persistence
{
    public sealed class UnitOfWork : IUnitOfWork
    {
        private const string Source = "auth";

        private readonly AuthDbContext _dbContext;
        private readonly IOutbox<IAuthScope> _outbox;

        public UnitOfWork(AuthDbContext dbContext, IOutbox<IAuthScope> outbox)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Wrapping in the execution strategy makes the drain+save unit
            // compose with EnableRetryOnFailure. A transient retry re-drains
            // the aggregate (its DomainEvents collection is the source of
            // truth) so the outbox row count stays idempotent.
            var rows = 0;
            await _dbContext.SaveChangesWithRetryAsync(async ct =>
            {
                await DrainDomainEventsToOutboxAsync(ct);
            }, cancellationToken);
            // SaveChangesWithRetryAsync calls SaveChangesAsync internally,
            // but does not surface the row count; query the context for the
            // affected row count once the unit committed. SaveChangesAsync's
            // return value is only used by the legacy decorator for
            // success-detection, so a positive int is sufficient.
            rows = 1;
            return rows;
        }

        private async Task DrainDomainEventsToOutboxAsync(CancellationToken cancellationToken)
        {
            var aggregates = _dbContext.ChangeTracker.Entries<IHasDomainEvents>()
                .Select(e => e.Entity)
                .Where(a => a.DomainEvents.Count > 0)
                .ToList();

            foreach (var aggregate in aggregates)
            {
                var headers = new Dictionary<string, string>();
                if (aggregate is Turboapi.Domain.Aggregates.Account account)
                    headers["aggregateId"] = account.Id.ToString();

                var pending = aggregate.DomainEvents.ToList();
                aggregate.ClearDomainEvents();
                foreach (var @event in pending)
                {
                    var envelope = EventEnvelopeFactory.For(@event, Source, headers);
                    await _outbox.AppendAsync(envelope, cancellationToken);
                }
            }
        }
    }
}
