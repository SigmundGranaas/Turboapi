using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using TurboApi.Data.Entity;
using Turboapi.services;

namespace Turboapi.auth;


public class RefreshTokenProvider : IAuthenticationProvider
{
    private readonly AuthDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ILogger<RefreshTokenProvider> _logger;

    public string Name => "RefreshToken";

    public RefreshTokenProvider(
        AuthDbContext context,
        IJwtService jwtService,
        ILogger<RefreshTokenProvider> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(IAuthenticationCredentials credentials)
    {
        if (credentials is not RefreshTokenCredentials refreshTokenCreds)
            throw new ArgumentException("Invalid credentials type");

        try
        {
            // First verify if token exists and is not revoked
            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshTokenCreds.RefreshToken);
            
            if (storedToken == null)
                throw new AuthenticationException("Invalid refresh token");
            
            if (storedToken.IsRevoked)
                throw new AuthenticationException("Token has been revoked");

            // Then attempt refresh
            var result = await _jwtService.RefreshTokenAsync(refreshTokenCreds.RefreshToken);
        
            // Mark old token as used/revoked
            storedToken.IsRevoked = true;
            await _context.SaveChangesAsync();
        
            return result;
        }
        catch (AuthenticationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during refresh token authentication");
            throw new AuthenticationException("Failed to refresh token", ex);
        }
    }

    public Task<AuthResult> RegisterAsync(string email, string password)
    {
        throw new NotSupportedException("Registration not supported for refresh token authentication");
    }
}