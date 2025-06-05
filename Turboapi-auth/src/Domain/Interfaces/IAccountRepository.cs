using Turboapi.Domain.Aggregates;

namespace Turboapi.Domain.Interfaces
{
    public interface IAccountRepository
    {
        Task<Account?> GetByIdAsync(Guid id);
        Task<Account?> GetByEmailAsync(string email);
        Task<Account?> GetByOAuthAsync(string providerName, string externalUserId);
        Task AddAsync(Account account);
        Task UpdateAsync(Account account);
    }
}