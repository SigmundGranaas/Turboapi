
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Turboapi.auth;
using Turboapi.core;
using Turboapi.Models;
using TurboApi.Data.Entity;
using Turboapi.services;
using Xunit;

namespace Turboapi.Tests;

public class PasswordAuthenticationProviderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AuthDbContext _context;
    private readonly PasswordAuthenticationProvider _provider;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;

    public PasswordAuthenticationProviderTests()
    {
        // Create and open a SQLite in-memory connection
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Configure the context to use SQLite
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create context and create schema
        _context = new AuthDbContext(options);
        _context.Database.EnsureCreated();

        // Create real implementations of services
        _passwordHasher = new PasswordHasher();
        var conf = new JwtConfig
        {
            Key = "your-super-secret-key-with-sufficient-length-for-testing-purposes-12345",
            Issuer = "test-issuer",
            Audience = "test-audience",
            TokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };
        

        // Create logger (using minimal logging for tests)
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<PasswordAuthenticationProvider>();
        
        var jwtOptions = Options.Create(conf);
        _jwtService = new JwtService(jwtOptions, _context, loggerFactory.CreateLogger<JwtService>());

        // Create the provider
        _provider = new PasswordAuthenticationProvider(_context, _jwtService, _passwordHasher, logger);
    }

    [Fact]
    public async Task RegisterAsync_WithValidCredentials_CreatesAccount()
    {
        // Arrange
        var email = "test@example.com";
        var password = "TestPassword123!";

        // Act
        var result = await _provider.RegisterAsync(email, password);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.AccountId);
        Assert.NotNull(result.Token);
        Assert.NotNull(result.RefreshToken);

        // Verify account was created
        var account = await _context.Accounts
            .Include(a => a.AuthenticationMethods)
            .Include(a => a.Roles)
            .FirstOrDefaultAsync(a => a.Email == email);

        Assert.NotNull(account);
        Assert.Single(account.AuthenticationMethods);
        Assert.Single(account.Roles);
        Assert.Equal(Roles.User, account.Roles.First().Role);
        
        var authMethod = account.AuthenticationMethods.First() as PasswordAuthentication;
        Assert.NotNull(authMethod);
        Assert.Equal("Password", authMethod.Provider);
        Assert.NotNull(authMethod.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ReturnsFalse()
    {
        // Arrange
        var email = "duplicate@example.com";
        var password = "TestPassword123!";

        // First registration should succeed
        var firstResult = await _provider.RegisterAsync(email, password);
        Assert.True(firstResult.Success);

        // Act
        var secondResult = await _provider.RegisterAsync(email, "DifferentPassword123!");

        // Assert
        Assert.False(secondResult.Success);
        Assert.Equal("Email already registered", secondResult.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var email = "auth@example.com";
        var password = "TestPassword123!";

        // Register first
        await _provider.RegisterAsync(email, password);

        // Act
        var result = await _provider.AuthenticateAsync(new PasswordCredentials(email, password));

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.NotNull(result.RefreshToken);

        // Verify LastLoginAt was updated
        var account = await _context.Accounts
            .Include(a => a.AuthenticationMethods)
            .FirstAsync(a => a.Email == email);

        Assert.NotNull(account.LastLoginAt);
        var authMethod = account.AuthenticationMethods.First();
        Assert.NotNull(authMethod.LastUsedAt);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidPassword_ReturnsFalse()
    {
        // Arrange
        var email = "wrong@example.com";
        var password = "TestPassword123!";

        // Register first
        await _provider.RegisterAsync(email, password);

        // Act
        var result = await _provider.AuthenticateAsync(new PasswordCredentials(email, "WrongPassword"));

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid email or password", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_WithNonexistentEmail_ReturnsFalse()
    {
        // Act
        var result = await _provider.AuthenticateAsync(new PasswordCredentials("NotPresent@gmail.com", "invalid"));

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid email or password", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidCredentialsType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async ()  => 
            await _provider.AuthenticateAsync(new InvalidCredentials()));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

// Helper class for testing invalid credentials
public class InvalidCredentials : IAuthenticationCredentials { }