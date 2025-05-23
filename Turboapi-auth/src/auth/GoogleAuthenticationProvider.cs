using Microsoft.EntityFrameworkCore;
using TurboApi.Data.Entity;
using Turboapi.Models;
using Turboapi.services;

namespace Turboapi.auth;

public class GoogleAuthenticationProvider : IAuthenticationProvider
{
    private readonly AuthDbContext _context;
    private readonly IGoogleAuthenticationService _googleAuthService;
    private readonly IJwtService _jwtService;
    private readonly ILogger<GoogleAuthenticationProvider> _logger;

    public string Name => "Google";

    public GoogleAuthenticationProvider(
        AuthDbContext context,
        IGoogleAuthenticationService googleAuthService,
        IJwtService jwtService,
        ILogger<GoogleAuthenticationProvider> logger)
    {
        _context = context;
        _googleAuthService = googleAuthService;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(IAuthenticationCredentials credentials)
    {
        if (credentials is not GoogleCredentials googleCreds)
            throw new ArgumentException("Invalid credentials type");

        try
        {
            var tokenInfo = await _googleAuthService.ValidateIdTokenAsync(googleCreds.IdToken);
            if (!tokenInfo.IsValid)
                return new AuthResult { Success = false, ErrorMessage = tokenInfo.ErrorMessage };

            var authMethod = await _context.AuthenticationMethods
                .OfType<OAuthAuthentication>()
                .Include(a => a.Account)
                .FirstOrDefaultAsync(a => 
                    a.Provider == Name && 
                    a.ExternalUserId == tokenInfo.Subject);

            if (authMethod == null)
            {
                // Create new account for first-time Google users
                var account = new Account
                {
                    Email = tokenInfo.Email,
                    AuthenticationMethods = new List<AuthenticationMethod>
                    {
                        new OAuthAuthentication
                        {
                            Provider = Name,
                            ExternalUserId = tokenInfo.Subject,
                            LastUsedAt = DateTime.UtcNow
                        }
                    },
                    Roles = new List<UserRole>
                    {
                        new() { Role = "User" }
                    }
                };

                await _context.Accounts.AddAsync(account);
                await _context.SaveChangesAsync();

                return new AuthResult 
                { 
                    Success = true,
                    AccountId = account.Id,
                    Token = await _jwtService.GenerateTokenAsync(account),
                    RefreshToken = await _jwtService.GenerateRefreshTokenAsync(account)
                };
            }

            // Update user info
            authMethod.LastUsedAt = DateTime.UtcNow;
            
            // Update account information if needed
            if (authMethod.Account != null)
            {
                if (authMethod.Account.Email != tokenInfo.Email
                   )
                {
                    authMethod.Account.Email = tokenInfo.Email;
                }
            }   
            
            await _context.SaveChangesAsync();

            return new AuthResult
            {
                Success = true,
                AccountId = authMethod.Account.Id,
                Token = await _jwtService.GenerateTokenAsync(authMethod.Account),
                RefreshToken = await _jwtService.GenerateRefreshTokenAsync(authMethod.Account)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Google authentication");
            throw;
        }
    }

    // Required by interface but not used for Google
    public Task<AuthResult> RegisterAsync(string email, string password)
    {
        throw new NotSupportedException("Registration not supported for Google authentication");
    }
}