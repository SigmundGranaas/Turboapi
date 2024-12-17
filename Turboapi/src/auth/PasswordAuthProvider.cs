using Microsoft.EntityFrameworkCore;
using Turboapi.core;
using TurboApi.Data.Entity;
using Turboapi.Models;
using Turboapi.services;

namespace Turboapi.auth;

public class PasswordAuthenticationProvider : IAuthenticationProvider
{
    private readonly AuthDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<PasswordAuthenticationProvider> _logger;

    public string Name => "Password";

    public PasswordAuthenticationProvider(
        AuthDbContext context,
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        ILogger<PasswordAuthenticationProvider> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<AuthResult> AuthenticateAsync(IAuthenticationCredentials credentials)
    {
        if (credentials is not PasswordCredentials passwordCreds)
            throw new ArgumentException("Invalid credentials type");
    
        if ( passwordCreds.IsRegistration)
        {
            return await RegisterAsync(passwordCreds.Email, passwordCreds.Password);
        }

        try
        {
            var authMethod = await _context.AuthenticationMethods
                .OfType<PasswordAuthentication>()
                .Include(a => a.Account)
                .FirstOrDefaultAsync(a => 
                    a.Account.Email == passwordCreds.Email &&
                    a.Provider == Name);

            if (authMethod == null)
                return new AuthResult { Success = false, ErrorMessage = "Invalid email or password" };

            if (!_passwordHasher.VerifyPassword(passwordCreds.Password, authMethod.PasswordHash))
                return new AuthResult { Success = false, ErrorMessage = "Invalid email or password" };

            // Update last used timestamp
            authMethod.LastUsedAt = DateTime.UtcNow;
            authMethod.Account.LastLoginAt = DateTime.UtcNow;
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
            _logger.LogError(ex, "Error during password authentication");
            throw;
        }
    }

    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        try
        {
            // Check if email already exists
            if (await _context.Accounts.AnyAsync(a => a.Email == email))
                return new AuthResult { Success = false, ErrorMessage = "Email already registered" };

            var hashedPassword = _passwordHasher.HashPassword(password);

            // Create new account with password authentication
            var account = new Account
            {
                Email = email,
                AuthenticationMethods = new List<AuthenticationMethod>
                {
                    new PasswordAuthentication
                    {
                        Provider = Name,
                        PasswordHash = hashedPassword
                    }
                },
                Roles = new List<UserRole>
                {
                    new() { Role = Roles.User }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password registration");
            throw;
        }
    }
}