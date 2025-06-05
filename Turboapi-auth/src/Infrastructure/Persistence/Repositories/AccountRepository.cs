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

        public async Task<Account?> GetByOAuthAsync(string providerName, string externalUserId)
        {
            return await _context.Accounts
                .Include(a => a.Roles)
                .Include(a => a.AuthenticationMethods.OfType<OAuthAuthMethod>()) // Eager load OAuth methods
                .Include(a => a.RefreshTokens)
                .FirstOrDefaultAsync(a =>
                    a.AuthenticationMethods.OfType<OAuthAuthMethod>()
                     .Any(oam => oam.ProviderName == providerName && oam.ExternalUserId == externalUserId));
        }

        public async Task AddAsync(Account account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            
            await _context.Accounts.AddAsync(account);
        }

        public async Task UpdateAsync(Account account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            _context.Accounts.Update(account);
            await Task.CompletedTask; 
        }
    }
}