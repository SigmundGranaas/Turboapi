using Microsoft.EntityFrameworkCore;
using Turbo.Messaging;
using Turbo.Outbox;
using Turboapi.Application.Interfaces;
using Turboapi.Domain;

namespace Turboapi.Infrastructure.Persistence
{
    public sealed class UnitOfWork : IUnitOfWork
    {
        private const string Source = "auth";

        private readonly AuthDbContext _dbContext;
        private readonly IOutbox _outbox;

        public UnitOfWork(AuthDbContext dbContext, IOutbox outbox)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await DrainDomainEventsToOutboxAsync(cancellationToken);
            return await _dbContext.SaveChangesAsync(cancellationToken);
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