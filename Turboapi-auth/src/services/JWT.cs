using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using Turboapi.auth;
using Turboapi.Models;

namespace Turboapi.services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TurboApi.Data.Entity;

public class JwtConfig
{
    public string Key { get; set; }
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public int TokenExpirationMinutes { get; set; }
    public int RefreshTokenExpirationDays { get; set; }
}

public interface IJwtService
{
    Task<string> GenerateTokenAsync(Account account);
    Task<string> GenerateRefreshTokenAsync(Account account);
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);
    Task<(bool isValid, string? userId)> ValidateRefreshTokenAsync(string refreshToken);
    Task<AuthResult> RefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken, string reason = "User logout");
}

public class JwtService : IJwtService
{
    private readonly JwtConfig _jwtConfig;
    private readonly AuthDbContext _context;
    private readonly ILogger<JwtService> _logger;

    public JwtService(
        IOptions<JwtConfig> jwtConfig,
        AuthDbContext context,
        ILogger<JwtService> logger)
    {
        _jwtConfig = jwtConfig.Value;
        _context = context;
        _logger = logger;
    }
    
    public async Task<AuthResult> RefreshTokenAsync(string refreshToken)
{
    try
    {
        var storedToken = await _context.RefreshTokens
            .Include(t => t.Account)
            .ThenInclude(a => a.Roles)
            .FirstOrDefaultAsync(t => t.Token == refreshToken);

        if (storedToken == null)
            return new AuthResult 
            { 
                Success = false, 
                ErrorMessage = "Invalid refresh token" 
            };

        if (storedToken.IsRevoked)
            return new AuthResult 
            { 
                Success = false, 
                ErrorMessage = "Refresh token has been revoked" 
            };

        if (storedToken.ExpiryTime < DateTime.UtcNow)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedReason = "Token expired";
            await _context.SaveChangesAsync();
            
            return new AuthResult 
            { 
                Success = false, 
                ErrorMessage = "Refresh token has expired" 
            };
        }

        // Mark current token as revoked BEFORE generating new ones
        storedToken.IsRevoked = true;
        storedToken.RevokedReason = "Token refreshed";
        await _context.SaveChangesAsync();

        // Generate new tokens
        var newAccessToken = await GenerateTokenAsync(storedToken.Account);
        var newRefreshToken = await GenerateRefreshTokenAsync(storedToken.Account);

        return new AuthResult
        {
            Success = true,
            Token = newAccessToken,
            RefreshToken = newRefreshToken,
            AccountId = storedToken.AccountId
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error refreshing token");
        throw new AuthenticationException("An error occurred while refreshing the token", ex);
    }
}
    
    public async Task<string> GenerateTokenAsync(Account account)
    {
        var roles = await _context.UserRoles
            .Where(r => r.AccountId == account.Id)
            .Select(r => r.Role)
            .ToListAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Email, account.Email),
            // Add unique JWT ID
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // Add issued at time
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtConfig.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtConfig.Issuer,
            audience: _jwtConfig.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_jwtConfig.TokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

public async Task<string> GenerateRefreshTokenAsync(Account account)
{
    var randomNumber = new byte[64];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(randomNumber);
    var refreshToken = Convert.ToBase64String(randomNumber);

    // Create new refresh token first
    var newToken = new RefreshToken
    {
        AccountId = account.Id,
        Token = refreshToken,
        ExpiryTime = DateTime.UtcNow.AddDays(_jwtConfig.RefreshTokenExpirationDays),
        CreatedAt = DateTime.UtcNow,
        IsRevoked = false
    };

    await _context.RefreshTokens.AddAsync(newToken);
    
    // Then revoke existing tokens
    var existingTokens = await _context.RefreshTokens
        .Where(t => t.AccountId == account.Id && !t.IsRevoked && t.Id != newToken.Id)
        .ToListAsync();

    foreach (var token in existingTokens)
    {
        token.IsRevoked = true;
        token.RevokedReason = "Token rotation";
    }

    await _context.SaveChangesAsync();
    return refreshToken;
}
    
    public async Task<(bool isValid, string? userId)> ValidateRefreshTokenAsync(string refreshToken)
    {
        try
        {
            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken && !t.IsRevoked);

            if (storedToken == null)
                return (false, null);

            if (storedToken.ExpiryTime < DateTime.UtcNow)
            {
                storedToken.IsRevoked = true;
                storedToken.RevokedReason = "Token expired";
                await _context.SaveChangesAsync();
                return (false, null);
            }

            return (true, storedToken.AccountId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating refresh token");
            return (false, null);
        }
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, string reason = "User logout")
    {
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == refreshToken);

        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedReason = reason;
            await _context.SaveChangesAsync();
        }
    }
    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtConfig.Key);

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidIssuer = _jwtConfig.Issuer,
                ValidAudience = _jwtConfig.Audience,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken || 
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, 
                    StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation failed");
            return null;
        }
    }
}