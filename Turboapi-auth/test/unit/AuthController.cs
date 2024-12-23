using Microsoft.AspNetCore.Mvc;
using Turboapi.auth;
using Turboapi.dto;
using Turboapi.services;
using Xunit;

namespace Turboapi.Tests;

public class AuthControllerTests
{
    private readonly TestAuthenticationService _authService;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _authService = new TestAuthenticationService();
        _controller = new AuthController(
            _authService,
            loggerFactory.CreateLogger<AuthController>());
    }

    [Fact]
    public async Task Register_WithMatchingPasswords_ReturnsSuccess()
    {
        // Arrange
        var request = new RegisterRequest("test@example.com", "password123", "password123");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = true,
            Token = "test-token",
            RefreshToken = "test-refresh-token"
        });

        // Act
        var actionResult = await _controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        
        Assert.True(response.Success);
        Assert.Equal("test-token", response.AccessToken);
        Assert.Equal("test-refresh-token", response.RefreshToken);
        Assert.Null(response.Error);

        // Verify correct credentials were passed
        var (provider, credentials) = _authService.LastAuthenticationAttempt!.Value;
        Assert.Equal("Password", provider);
        var passwordCreds = Assert.IsType<PasswordCredentials>(credentials);
        Assert.Equal(request.Email, passwordCreds.Email);
        Assert.Equal(request.Password, passwordCreds.Password);
        Assert.True(passwordCreds.IsRegistration);
    }

    [Fact]
    public async Task Register_WithMismatchedPasswords_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterRequest("test@example.com", "password123", "different123");

        // Act
        var actionResult = await _controller.Register(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(badRequestResult.Value);
        
        Assert.False(response.Success);
        Assert.Equal("Passwords do not match", response.Error);
        Assert.Null(_authService.LastAuthenticationAttempt);
    }

    [Fact]
    public async Task Register_WhenAuthenticationFails_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterRequest("test@example.com", "password123", "password123");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = false,
            ErrorMessage = "Email already registered"
        });

        // Act
        var actionResult = await _controller.Register(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(badRequestResult.Value);
        
        Assert.False(response.Success);
        Assert.Equal("Email already registered", response.Error);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var request = new LoginRequest("test@example.com", "password123");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = true,
            Token = "test-token",
            RefreshToken = "test-refresh-token"
        });

        // Act
        var actionResult = await _controller.Login(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        
        Assert.True(response.Success);
        Assert.Equal("test-token", response.AccessToken);
        Assert.Equal("test-refresh-token", response.RefreshToken);
        Assert.Null(response.Error);

        // Verify correct credentials were passed
        var (provider, credentials) = _authService.LastAuthenticationAttempt!.Value;
        Assert.Equal("Password", provider);
        var passwordCreds = Assert.IsType<PasswordCredentials>(credentials);
        Assert.Equal(request.Email, passwordCreds.Email);
        Assert.Equal(request.Password, passwordCreds.Password);
        Assert.False(passwordCreds.IsRegistration);
    }

    [Fact]
    public async Task Login_WhenAuthenticationFails_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest("test@example.com", "wrongpassword");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = false,
            ErrorMessage = "Invalid email or password"
        });

        // Act
        var actionResult = await _controller.Login(request);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(unauthorizedResult.Value);
        
        Assert.False(response.Success);
        Assert.Equal("Invalid email or password", response.Error);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var request = new RefreshTokenRequest("valid-refresh-token");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = true,
            Token = "new-token",
            RefreshToken = "new-refresh-token"
        });

        // Act
        var actionResult = await _controller.RefreshToken(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        
        Assert.True(response.Success);
        Assert.Equal("new-token", response.AccessToken);
        Assert.Equal("new-refresh-token", response.RefreshToken);
        Assert.Null(response.Error);

        // Verify correct credentials were passed
        var (provider, credentials) = _authService.LastAuthenticationAttempt!.Value;
        Assert.Equal("RefreshToken", provider);
        var refreshCreds = Assert.IsType<RefreshTokenCredentials>(credentials);
        Assert.Equal(request.RefreshToken, refreshCreds.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_WhenTokenInvalid_ReturnsUnauthorized()
    {
        // Arrange
        var request = new RefreshTokenRequest("invalid-refresh-token");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = false,
            ErrorMessage = "Invalid refresh token"
        });

        // Act
        var actionResult = await _controller.RefreshToken(request);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(unauthorizedResult.Value);
        
        Assert.False(response.Success);
        Assert.Equal("Invalid refresh token", response.Error);
    }
}

// Test Double
public class TestAuthenticationService : IAuthenticationService
{
    private AuthResult? _nextResult;
    public (string Provider, IAuthenticationCredentials Credentials)? LastAuthenticationAttempt { get; private set; }

    public void SetupAuthenticationResult(AuthResult result)
    {
        _nextResult = result;
    }

    public Task<AuthResult> AuthenticateAsync(string provider, IAuthenticationCredentials credentials)
    {
        LastAuthenticationAttempt = (provider, credentials);
        return Task.FromResult(_nextResult ?? new AuthResult { Success = false });
    }
    
        public Task<AuthResult> LinkAuthenticationMethodAsync(
        Guid accountId, 
        string provider, 
        IAuthenticationCredentials credentials)
    {
        throw new NotImplementedException();

    }

    public Task<bool> RemoveAuthenticationMethodAsync(Guid accountId, Guid authMethodId)
    {
        throw new NotImplementedException();
    }
}