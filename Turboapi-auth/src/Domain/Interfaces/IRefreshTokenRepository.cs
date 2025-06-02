using Turboapi.Domain.Aggregates;

namespace Turboapi.Domain.Interfaces
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenAsync(string token);
        Task AddAsync(RefreshToken refreshToken);
        Task UpdateAsync(RefreshToken refreshToken); // For revoking or updating other properties
        Task<IEnumerable<RefreshToken>> GetActiveTokensForAccountAsync(Guid accountId);
    }
}