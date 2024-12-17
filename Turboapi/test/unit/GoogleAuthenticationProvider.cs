using System.Security.Claims;
using Turboapi.auth;
using Turboapi.Models;
using Turboapi.services;
using Turboapi.Services;
using Xunit;

namespace Turboapi.test.unit;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TurboApi.Data.Entity;

public class GoogleAuthenticationProviderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AuthDbContext _context;
    private readonly TestGoogleTokenValidator _tokenValidator;
    private readonly TestJwtService _jwtService;
    private readonly GoogleAuthenticationProvider _provider;

    public GoogleAuthenticationProviderTests()
    {
        // Setup SQLite in-memory database
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AuthDbContext(options);
        _context.Database.EnsureCreated();

        // Setup test doubles
        var settings = Options.Create(new GoogleAuthSettings
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            TokenInfoEndpoint = "https://test.endpoint/tokeninfo"
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _tokenValidator = new TestGoogleTokenValidator(
            settings,
            new HttpClient(),
            loggerFactory.CreateLogger<GoogleTokenValidator>());

        _jwtService = new TestJwtService();

        // Create the provider
        _provider = new GoogleAuthenticationProvider(
            _context,
            _tokenValidator,
            _jwtService,
            loggerFactory.CreateLogger<GoogleAuthenticationProvider>());
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidToken_FirstTimeUser_CreatesAccount()
    {
        // Arrange
        var credentials = new GoogleCredentials("valid-token");
        
        _tokenValidator.SetupTokenResponse(new GoogleTokenResponse
        {
            Aud = "test-client-id",
            Sub = "google-user-123",
            Email = "test@gmail.com",
            EmailVerified = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        // Act
        var result = await _provider.AuthenticateAsync(credentials);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        Assert.Equal("test-jwt-token", result.Token);
        Assert.Equal("test-refresh-token", result.RefreshToken);

        // Verify account was created
        var account = await _context.Accounts
            .Include(a => a.AuthenticationMethods)
            .Include(a => a.Roles)
            .FirstAsync(a => a.Email == "test@gmail.com");

        Assert.NotNull(account);
        Assert.Single(account.Roles);
        Assert.Equal("User", account.Roles[0].Role);

        var authMethod = Assert.IsType<OAuthAuthentication>(account.AuthenticationMethods[0]);
        Assert.Equal("Google", authMethod.Provider);
        Assert.Equal("google-user-123", authMethod.ExternalUserId);
        Assert.Equal("valid-token", authMethod.AccessToken);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidToken_ExistingUser_UpdatesToken()
    {
        // Arrange
        var existingAccount = new Account
        {
            Email = "existing@gmail.com",
            AuthenticationMethods = new List<AuthenticationMethod>
            {
                new OAuthAuthentication
                {
                    Provider = "Google",
                    ExternalUserId = "google-user-456",
                    AccessToken = "old-token"
                }
            },
            Roles = new List<UserRole>
            {
                new() { Role = "User" }
            }
        };

        await _context.Accounts.AddAsync(existingAccount);
        await _context.SaveChangesAsync();

        var credentials = new GoogleCredentials("new-valid-token");
        
        _tokenValidator.SetupTokenResponse(new GoogleTokenResponse
        {
            Aud = "test-client-id",
            Sub = "google-user-456",
            Email = "existing@gmail.com",
            EmailVerified = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        // Act
        var result = await _provider.AuthenticateAsync(credentials);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(existingAccount.Id, result.AccountId);
        
        // Verify token was updated
        var authMethod = await _context.AuthenticationMethods
            .OfType<OAuthAuthentication>()
            .FirstAsync(a => a.ExternalUserId == "google-user-456");

        Assert.Equal("new-valid-token", authMethod.AccessToken);
        Assert.NotNull(authMethod.LastUsedAt);
        Assert.True(DateTime.UtcNow.Subtract(authMethod.LastUsedAt.Value).TotalSeconds < 5);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidToken_ReturnsFalse()
    {
        // Arrange
        var credentials = new GoogleCredentials("invalid-token");
        
        _tokenValidator.SetupTokenResponse(new GoogleTokenResponse
        {
            Aud = "wrong-client-id",
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            EmailVerified = true
        });

        // Act
        var result = await _provider.AuthenticateAsync(credentials);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Token was not issued for this application", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidCredentialType_ThrowsArgumentException()
    {
        // Arrange
        var invalidCredentials = new InvalidCredentials();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _provider.AuthenticateAsync(invalidCredentials));
    }

    [Fact]
    public async Task RegisterAsync_Always_ThrowsNotSupportedException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(() => 
            _provider.RegisterAsync("test@example.com", "password"));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

// Test Doubles
public class TestJwtService : IJwtService
{
    public Task<string> GenerateTokenAsync(Account account)
        => Task.FromResult("test-jwt-token");

    public Task<string> GenerateRefreshTokenAsync(Account account)
        => Task.FromResult("test-refresh-token");

    public Task<AuthResult> RefreshTokenAsync(string refreshToken)
        => throw new NotImplementedException();

    public Task RevokeRefreshTokenAsync(string refreshToken, string reason = "User logout")
        => throw new NotImplementedException();

    public Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
        => throw new NotImplementedException();

    public Task<(bool isValid, string? userId)> ValidateRefreshTokenAsync(string refreshToken)
        => throw new NotImplementedException();
}

public class InvalidCredentials : IAuthenticationCredentials { }