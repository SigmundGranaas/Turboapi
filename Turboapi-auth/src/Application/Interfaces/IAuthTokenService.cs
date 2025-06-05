using System.Security.Claims;
using Turboapi.Application.Contracts.V1.Tokens;
using Turboapi.Domain.Aggregates;

namespace Turboapi.Application.Interfaces
{
    /// <summary>
    /// Represents the raw strings and expiry for a new set of tokens.
    /// </summary>
    public record NewTokenStrings(string AccessToken, string RefreshTokenValue, DateTime RefreshTokenExpiresAt);

    /// <summary>
    /// Defines the contract for services that handle JWT access and refresh token generation,
    /// validation, and processing.
    /// </summary>
    public interface IAuthTokenService
    {
        /// <summary>
        /// Generates a new pair of access and refresh tokens for the given account.
        /// The implementation is responsible for persisting the refresh token.
        /// Note: This is kept for compatibility with existing handlers but should be phased out.
        /// </summary>
        Task<TokenResult> GenerateTokensAsync(Account account);

        /// <summary>
        /// Generates new token strings and expiry information without any persistence.
        /// </summary>
        /// <param name="account">The account for which to generate token strings.</param>
        /// <returns>A record containing the new access token, refresh token value, and its expiry date.</returns>
        Task<NewTokenStrings> GenerateNewTokenStringsAsync(Account account);

        /// <summary>
        /// Validates the given JWT access token.
        /// </summary>
        Task<ClaimsPrincipal?> ValidateAccessTokenAsync(string token);
    }
}