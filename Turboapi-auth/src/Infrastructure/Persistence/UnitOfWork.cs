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
        private readonly IOutbox<AuthDbContext> _outbox;

        public UnitOfWork(AuthDbContext dbContext, IOutbox<AuthDbContext> outbox)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Wrapping in the execution strategy makes the drain+save unit
            // compose with EnableRetryOnFailure. Otherwise a transient
            // connection error on the second SaveChanges retry would
            // double-insert outbox rows. The strategy keeps the unit
            // idempotent across attempts.
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            var rows = 0;
            await strategy.ExecuteAsync(async ct =>
            {
                await DrainDomainEventsToOutboxAsync(ct);
                rows = await _dbContext.SaveChangesAsync(ct);
            }, cancellationToken);
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
