using System.Collections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Turboapi.auth;
using Turboapi.controller;
using Turboapi.Controller;
using Turboapi.dto;
using Turboapi.services;
using Xunit;

namespace Turboapi.Tests;

public class AuthControllerTests
{
    private readonly TestAuthenticationService _authService;
    private readonly AuthController _controller;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly DefaultHttpContext _httpContext;

    public AuthControllerTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _authService = new TestAuthenticationService();
        
        // Setup data protection
        var services = new ServiceCollection();
        services.AddDataProtection();
        var serviceProvider = services.BuildServiceProvider();
        _dataProtectionProvider = serviceProvider.GetRequiredService<IDataProtectionProvider>();

        // Setup HTTP context with Response.Cookies implementation
        _httpContext = new DefaultHttpContext();
        
        _controller = new AuthController(
            _authService,
            loggerFactory.CreateLogger<AuthController>(),
            new AuthHelper(_dataProtectionProvider, CreateConfiguration(), loggerFactory.CreateLogger<AuthHelper>()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = _httpContext
            }
        };
    }
    
    public IConfiguration CreateConfiguration()
    {
        var inMemorySettings = new Dictionary<string, string> {
            {"CookieSettings:Domain", "test.com"},
            {"CookieSettings:Secure", "true"},
            {"CookieSettings:HttpOnly", "true"},
            {"CookieSettings:SameSite", "Strict"}
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    private void VerifyCookies(string expectedAccessToken, string expectedRefreshToken)
    {
        var protector = _dataProtectionProvider.CreateProtector("AuthCookie.v1");
        var setCookieHeaders = _httpContext.Response.Headers["Set-Cookie"].ToList();
        
        // Find the cookies by name
        var accessTokenCookie = setCookieHeaders.FirstOrDefault(c => c.StartsWith("AccessToken="));
        var refreshTokenCookie = setCookieHeaders.FirstOrDefault(c => c.StartsWith("RefreshToken="));

        Assert.NotNull(accessTokenCookie);
        Assert.NotNull(refreshTokenCookie);

        // Extract values from the cookie strings
        string GetCookieValue(string cookie)
        {
            var mainPart = cookie.Split(';')[0];
            return mainPart.Substring(mainPart.IndexOf('=') + 1);
        }
        
        // Verify secure flags are present
        Assert.Contains("HttpOnly", accessTokenCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", accessTokenCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite", accessTokenCookie, StringComparison.OrdinalIgnoreCase);
        
        // Decrypt and verify token values
        var decryptedAccessToken = protector.Unprotect(GetCookieValue(accessTokenCookie));
        var decryptedRefreshToken = protector.Unprotect(GetCookieValue(refreshTokenCookie));

        Assert.Equal(expectedAccessToken, decryptedAccessToken);
        Assert.Equal(expectedRefreshToken, decryptedRefreshToken);
    }

    private void SetupEncryptedCookie(string name, string value)
    {
        var protector = _dataProtectionProvider.CreateProtector("AuthCookie.v1");
        var encryptedValue = protector.Protect(value);
        
        // Create a mock cookie collection using dictionary
        var cookieDictionary = new Dictionary<string, string> { { name, encryptedValue } };
        _httpContext.Request.Cookies = new TestCookieCollection(cookieDictionary);
    }
    
    private class TestCookieCollection : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _cookies;

        public TestCookieCollection(Dictionary<string, string> cookies)
        {
            _cookies = cookies;
        }

        public string? this[string key] => _cookies.TryGetValue(key, out var value) ? value : null;

        public int Count => _cookies.Count;

        public ICollection<string> Keys => _cookies.Keys;

        public bool ContainsKey(string key) => _cookies.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();

        public bool TryGetValue(string key, out string? value) => _cookies.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => _cookies.GetEnumerator();
    }


    [Fact]
    public async Task Register_WithMatchingPasswords_ReturnsSuccessAndSetsCookies()
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

        // Verify cookies were set correctly
        VerifyCookies("test-token", "test-refresh-token");

        // Verify correct credentials were passed
        var (provider, credentials) = _authService.LastAuthenticationAttempt!.Value;
        Assert.Equal("Password", provider);
        var passwordCreds = Assert.IsType<PasswordCredentials>(credentials);
        Assert.Equal(request.Email, passwordCreds.Email);
        Assert.Equal(request.Password, passwordCreds.Password);
        Assert.True(passwordCreds.IsRegistration);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsSuccessAndSetsCookies()
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

        // Verify cookies were set correctly
        VerifyCookies("test-token", "test-refresh-token");

        // Verify correct credentials were passed
        var (provider, credentials) = _authService.LastAuthenticationAttempt!.Value;
        Assert.Equal("Password", provider);
        var passwordCreds = Assert.IsType<PasswordCredentials>(credentials);
        Assert.Equal(request.Email, passwordCreds.Email);
        Assert.Equal(request.Password, passwordCreds.Password);
        Assert.False(passwordCreds.IsRegistration);
    }

    [Fact]
    public async Task RefreshToken_WithValidCookie_ReturnsSuccessAndUpdatesCookies()
    {
        // Arrange
        SetupEncryptedCookie("RefreshToken", "valid-refresh-token");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = true,
            Token = "new-token",
            RefreshToken = "new-refresh-token"
        });

        // Act
        var actionResult = await _controller.RefreshToken(null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        
        Assert.True(response.Success);
        Assert.Equal("new-token", response.AccessToken);
        Assert.Equal("new-refresh-token", response.RefreshToken);
        Assert.Null(response.Error);

        // Verify cookies were updated correctly
        VerifyCookies("new-token", "new-refresh-token");

        // Verify correct credentials were passed
        var (provider, credentials) = _authService.LastAuthenticationAttempt!.Value;
        Assert.Equal("RefreshToken", provider);
        var refreshCreds = Assert.IsType<RefreshTokenCredentials>(credentials);
        Assert.Equal("valid-refresh-token", refreshCreds.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_WithValidBodyToken_ReturnsSuccessAndUpdatesCookies()
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

        // Verify cookies were set correctly
        VerifyCookies("new-token", "new-refresh-token");

        // Verify correct credentials were passed
        var (provider, credentials) = _authService.LastAuthenticationAttempt!.Value;
        Assert.Equal("RefreshToken", provider);
        var refreshCreds = Assert.IsType<RefreshTokenCredentials>(credentials);
        Assert.Equal("valid-refresh-token", refreshCreds.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_PrefersCookieOverBody()
    {
        // Arrange
        SetupEncryptedCookie("RefreshToken", "cookie-refresh-token");
        var request = new RefreshTokenRequest("body-refresh-token");

        _authService.SetupAuthenticationResult(new AuthResult
        {
            Success = true,
            Token = "new-token",
            RefreshToken = "new-refresh-token"
        });

        // Act
        var actionResult = await _controller.RefreshToken(request);

        // Assert
        // Verify the cookie token was used instead of body token
        var (_, credentials) = _authService.LastAuthenticationAttempt!.Value;
        var refreshCreds = Assert.IsType<RefreshTokenCredentials>(credentials);
        Assert.Equal("cookie-refresh-token", refreshCreds.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_WithNoTokens_ReturnsUnauthorized()
    {
        // Act
        var actionResult = await _controller.RefreshToken();

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(actionResult.Result);
        var response = Assert.IsType<AuthResponse>(unauthorizedResult.Value);
        
        Assert.False(response.Success);
        Assert.Equal("No refresh token found", response.Error);
    }

    [Fact]
    public async Task Logout_ClearsCookies()
    {
        // Act
        var actionResult =  _controller.Logout();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var cookies = _httpContext.Response.Headers.SetCookie.ToList();
        
        // Verify expired cookies are set
        Assert.Contains(cookies, c => c.StartsWith("AccessToken=") && c.Contains("expires=Thu, 01 Jan 1970"));
        Assert.Contains(cookies, c => c.StartsWith("RefreshToken=") && c.Contains("expires=Thu, 01 Jan 1970"));
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