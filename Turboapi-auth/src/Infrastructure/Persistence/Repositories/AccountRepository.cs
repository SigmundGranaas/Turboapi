using Microsoft.EntityFrameworkCore;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Infrastructure.Persistence.Repositories
{
    public class AccountRepository : IAccountRepository
    {
        private readonly AuthDbContext _context;

        public AccountRepository(AuthDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<Account?> GetByIdAsync(Guid id)
        {
            return await _context.Accounts
                .Include(a => a.Roles)
                .Include(a => a.AuthenticationMethods)
                .Include(a => a.RefreshTokens)
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<Account?> GetByEmailAsync(string email)
        {
            var lowerEmail = email.ToLowerInvariant();
            return await _context.Accounts
                .Include(a => a.Roles)
                .Include(a => a.AuthenticationMethods)
                .Include(a => a.RefreshTokens)
                .FirstOrDefaultAsync(a => a.Email == lowerEmail);
        }

        public async Task AddAsync(Account account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            
            await _context.Accounts.AddAsync(account);
            // Note: SaveChangesAsync is typically called by a Unit of Work or at the end of an application service/use case
            // For this example, we'll assume it's handled outside this repository method or called if this is the only operation.
            // If a Unit of Work pattern is implemented later, SaveChangesAsync would be removed from here.
            // For now, let's include it for standalone repository functionality in early stages.
            // await _context.SaveChangesAsync(); 
            // --> Decision: SaveChangesAsync will be handled by the Application Layer (Use Case handlers) or a UnitOfWork.
        }

        public async Task UpdateAsync(Account account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            _context.Accounts.Update(account);
            // As with AddAsync, SaveChangesAsync is typically handled by a higher layer or Unit of Work.
            // await _context.SaveChangesAsync();
            // --> Decision: SaveChangesAsync will be handled by the Application Layer (Use Case handlers) or a UnitOfWork.
            await Task.CompletedTask; // To satisfy async method signature if not calling SaveChangesAsync
        }
    }
}