using System.Security.Claims;
using Turboapi.Application.Results;             // For Result
using Turboapi.Application.Contracts.V1.Tokens; // For TokenResult
using Turboapi.Domain.Aggregates;             // For Account
using Turboapi.Application.Results.Errors;    // For RefreshTokenError

namespace Turboapi.Application.Interfaces
{
    /// <summary>
    /// Defines the contract for services that handle JWT access and refresh token generation,
    /// validation, and processing.
    /// </summary>
    public interface IAuthTokenService
    {
        /// <summary>
        /// Generates a new pair of access and refresh tokens for the given account.
        /// The implementation is responsible for persisting the refresh token.
        /// </summary>
        /// <param name="account">The account for whom to generate tokens. 
        /// This account object should have its roles and other necessary information pre-loaded.</param>
        /// <returns>A TokenResult containing the new access token, refresh token string, and account ID.</returns>
        Task<TokenResult> GenerateTokensAsync(Account account);

        /// <summary>
        /// Validates the given JWT access token.
        /// </summary>
        /// <param name="token">The access token string to validate.</param>
        /// <returns>
        /// A ClaimsPrincipal if the token is valid and not expired; otherwise, null.
        /// No distinction is made here between expired, malformed, or invalid signature for simplicity from the caller's perspective.
        /// The implementation should log the specific reason for validation failure.
        /// </returns>
        Task<ClaimsPrincipal?> ValidateAccessTokenAsync(string token);

        /// <summary>
        /// Validates a refresh token string, and if valid, revokes it and generates a new pair of
        /// access and refresh tokens (token rotation).
        /// </summary>
        /// <param name="refreshTokenString">The refresh token string to process.</param>
        /// <returns>
        /// A successful Result with a new TokenResult if processing and rotation are successful.
        /// A failed Result with a RefreshTokenError if the token is invalid, expired, revoked,
        /// or if an error occurs during processing (e.g., account not found, storage failure).
        /// </returns>
        Task<Result<TokenResult, RefreshTokenError>> ValidateAndProcessRefreshTokenAsync(string refreshTokenString);
    }
}