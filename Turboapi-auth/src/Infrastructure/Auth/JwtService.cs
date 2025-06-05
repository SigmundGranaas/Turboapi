using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Turboapi.Application.Contracts.V1.Tokens;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Interfaces;
using Turboapi.Application.Results.Errors;

namespace Turboapi.Infrastructure.Auth
{
    public class JwtService : IAuthTokenService
    {
        private readonly JwtConfig _jwtConfig;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IAccountRepository _accountRepository;
        private readonly ILogger<JwtService> _logger;

        public JwtService(
            IOptions<JwtConfig> jwtConfig,
            IRefreshTokenRepository refreshTokenRepository,
            IAccountRepository accountRepository,
            ILogger<JwtService> logger)
        {
            _jwtConfig = jwtConfig?.Value ?? throw new ArgumentNullException(nameof(jwtConfig));
            _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<TokenResult> GenerateTokensAsync(Account account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            // 1. Generate JWT Access Token
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, account.Id.ToString()), // Standard subject claim
                new(JwtRegisteredClaimNames.Email, account.Email),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // JWT ID
                new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) // Issued at
            };
            foreach (var role in account.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtConfig.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(_jwtConfig.TokenExpirationMinutes);

            var accessTokenDescriptor = new JwtSecurityToken(
                issuer: _jwtConfig.Issuer,
                audience: _jwtConfig.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: creds
            );
            var accessTokenString = new JwtSecurityTokenHandler().WriteToken(accessTokenDescriptor);

            // 2. Generate Refresh Token String
            var randomNumber = new byte[64]; // For a 512-bit random number
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
            }
            var refreshTokenString = Convert.ToBase64String(randomNumber);

            // 3. Create RefreshToken domain entity
            var refreshTokenEntity = RefreshToken.Create(
                account.Id,
                refreshTokenString,
                DateTime.UtcNow.AddDays(_jwtConfig.RefreshTokenExpirationDays)
            );

            // 4. (Optional but recommended) Revoke existing active refresh tokens for this account
            // This implements a "one active refresh token per account" or "family rotation" strategy.
            var existingActiveTokens = await _refreshTokenRepository.GetActiveTokensForAccountAsync(account.Id);
            foreach (var existingToken in existingActiveTokens)
            {
                existingToken.Revoke("New token pair generated");
                await _refreshTokenRepository.UpdateAsync(existingToken);
                 _logger.LogInformation("Revoked existing refresh token {RefreshTokenId} for account {AccountId} due to new token generation.", existingToken.Id, account.Id);
            }
            
            // 5. Persist the new refresh token entity
            await _refreshTokenRepository.AddAsync(refreshTokenEntity);
            // Note: SaveChangesAsync will be called by the Unit of Work in the application layer

            _logger.LogInformation("Generated new access and refresh token for account {AccountId}", account.Id);

            return new TokenResult(accessTokenString, refreshTokenString, account.Id);
        }

        public Task<ClaimsPrincipal?> ValidateAccessTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Task.FromResult<ClaimsPrincipal?>(null);

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtConfig.Key);
            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _jwtConfig.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _jwtConfig.Audience,
                    ValidateLifetime = true, // Ensure token is not expired
                    ClockSkew = TimeSpan.Zero // No clock skew allowed
                }, out SecurityToken validatedToken);

                if (validatedToken is not JwtSecurityToken jwtSecurityToken ||
                    !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogWarning("Access token validation failed: Invalid algorithm or token type.");
                    return Task.FromResult<ClaimsPrincipal?>(null);
                }
                
                _logger.LogDebug("Access token validated successfully for subject: {Subject}", principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
                return Task.FromResult<ClaimsPrincipal?>(principal);
            }
            catch (SecurityTokenExpiredException ex)
            {
                _logger.LogInformation("Access token validation failed: Token expired. {ExceptionMessage}", ex.Message);
                // Potentially return a specific result or allow null to indicate failure
                return Task.FromResult<ClaimsPrincipal?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Access token validation failed: {ErrorMessage}", ex.Message);
                return Task.FromResult<ClaimsPrincipal?>(null);
            }
        }

        public async Task<Result<TokenResult, RefreshTokenError>> ValidateAndProcessRefreshTokenAsync(string refreshTokenString)
        {
            if (string.IsNullOrWhiteSpace(refreshTokenString))
            {
                return RefreshTokenError.InvalidToken;
            }

            var storedToken = await _refreshTokenRepository.GetByTokenAsync(refreshTokenString);

            if (storedToken == null)
            {
                _logger.LogWarning("Refresh token validation failed: Token not found in repository. Token: {RefreshTokenSubstring}", refreshTokenString.Length > 10 ? refreshTokenString.Substring(0,10) + "..." : refreshTokenString);
                return RefreshTokenError.InvalidToken;
            }

            if (storedToken.IsRevoked)
            {
                _logger.LogWarning("Refresh token validation failed: Token {RefreshTokenId} is already revoked. Reason: {RevokedReason}", storedToken.Id, storedToken.RevokedReason);
                return RefreshTokenError.Revoked;
            }

            if (storedToken.IsExpired)
            {
                _logger.LogWarning("Refresh token validation failed: Token {RefreshTokenId} is expired. ExpiresAt: {ExpiresAt}", storedToken.Id, storedToken.ExpiresAt);
                // Optionally, revoke it formally in the DB if not already done by a background job
                // storedToken.Revoke("Expired on validation");
                // await _refreshTokenRepository.UpdateAsync(storedToken);
                return RefreshTokenError.Expired;
            }

            // Token is valid, proceed with rotation
            try
            {
                // 1. Mark current token as revoked (this is crucial for security)
                storedToken.Revoke("Rotated: New token pair issued");
                await _refreshTokenRepository.UpdateAsync(storedToken);

                // 2. Fetch the account associated with the token
                var account = await _accountRepository.GetByIdAsync(storedToken.AccountId);
                if (account == null)
                {
                    _logger.LogError("Refresh token processing failed: Account {AccountId} not found for valid refresh token {RefreshTokenId}.", storedToken.AccountId, storedToken.Id);
                    // This is a severe state inconsistency. The refresh token should not exist if the account doesn't.
                    return RefreshTokenError.AccountNotFound; 
                }

                // 3. Generate new access and refresh tokens
                // This reuses the GenerateTokensAsync logic but we need to avoid its internal revocation of other tokens
                // if we want a strict one-for-one rotation.
                // For simplicity, let's directly generate new ones here, similar to GenerateTokensAsync parts.

                var newAccessTokenString = await GenerateAccessTokenInternalAsync(account);
                var newRefreshTokenStringValue = GenerateRefreshTokenStringInternal();

                var newRefreshTokenEntity = RefreshToken.Create(
                    account.Id,
                    newRefreshTokenStringValue,
                    DateTime.UtcNow.AddDays(_jwtConfig.RefreshTokenExpirationDays)
                );
                await _refreshTokenRepository.AddAsync(newRefreshTokenEntity);
                // Note: SaveChangesAsync will be called by the Unit of Work

                _logger.LogInformation("Successfully processed and rotated refresh token for account {AccountId}. Old token {OldTokenId}, new token {NewTokenId}.", account.Id, storedToken.Id, newRefreshTokenEntity.Id);
                return new TokenResult(newAccessTokenString, newRefreshTokenStringValue, account.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during refresh token processing and rotation for token ID {RefreshTokenId}", storedToken?.Id);
                return RefreshTokenError.StorageFailure; // Or a more generic error
            }
        }

        // Internal helper for generating just the access token string
        private Task<string> GenerateAccessTokenInternalAsync(Account account)
        {
             var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, account.Email),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };
            foreach (var role in account.Roles) // Assumes Roles are loaded with the Account
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtConfig.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(_jwtConfig.TokenExpirationMinutes);

            var accessTokenDescriptor = new JwtSecurityToken(
                issuer: _jwtConfig.Issuer,
                audience: _jwtConfig.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: creds
            );
            return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(accessTokenDescriptor));
        }
        
        // Internal helper for generating just the refresh token string
        private string GenerateRefreshTokenStringInternal()
        {
            var randomNumber = new byte[64];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
            }
            return Convert.ToBase64String(randomNumber);
        }
    }
}