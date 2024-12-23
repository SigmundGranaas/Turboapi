using Microsoft.EntityFrameworkCore;
using Turboapi.auth;
using TurboApi.Data.Entity;

namespace Turboapi.services;

public class AuthenticationService : IAuthenticationService
{
    private readonly AuthDbContext _context;
    private readonly IEnumerable<IAuthenticationProvider> _providers;
    private readonly ILogger<AuthenticationService> _logger;
    
    public AuthenticationService(
        AuthDbContext context,
        IEnumerable<IAuthenticationProvider> providers,
        ILogger<AuthenticationService> logger)
    {
        _context = context;
        _providers = providers;
        _logger = logger;
    }
    
    public async Task<AuthResult> AuthenticateAsync(string provider, IAuthenticationCredentials credentials)
    {
        try
        {
            var authProvider = _providers.FirstOrDefault(p => p.Name == provider)
                ?? throw new ArgumentException($"Provider {provider} not supported");

            return await authProvider.AuthenticateAsync(credentials);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for provider {Provider}", provider);
            throw;
        }
    }

    public async Task<AuthResult> LinkAuthenticationMethodAsync(
        Guid accountId, 
        string provider, 
        IAuthenticationCredentials credentials)
    {
        try
        {
            var account = await _context.Accounts
                .Include(a => a.AuthenticationMethods)
                .FirstOrDefaultAsync(a => a.Id == accountId);

            if (account == null)
                return new AuthResult { Success = false, ErrorMessage = "Account not found" };

            var authProvider = _providers.FirstOrDefault(p => p.Name == provider)
                ?? throw new ArgumentException($"Provider {provider} not supported");

            var result = await authProvider.AuthenticateAsync(credentials);
            if (!result.Success)
                return result;

            // Link the new authentication method
            // Implementation depends on provider type
            return new AuthResult { Success = true, AccountId = accountId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to link authentication method");
            throw;
        }
    }

    public async Task<bool> RemoveAuthenticationMethodAsync(Guid accountId, Guid authMethodId)
    {
        try
        {
            var account = await _context.Accounts
                .Include(a => a.AuthenticationMethods)
                .FirstOrDefaultAsync(a => a.Id == accountId);

            if (account == null)
                return false;

            var authMethod = account.AuthenticationMethods
                .FirstOrDefault(am => am.Id == authMethodId);

            if (authMethod == null)
                return false;

            // Don't remove the last authentication method
            if (account.AuthenticationMethods.Count == 1)
                return false;

            _context.AuthenticationMethods.Remove(authMethod);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError// Services/AuthenticationService.cs
(ex, "Failed to remove authentication method");
            throw;
        }
    }
}