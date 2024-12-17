using System.Text.Json;
using Microsoft.Extensions.Options;
using Turboapi.auth;
using Turboapi.Models;
using Turboapi.Services;
using Xunit;

public class GoogleTokenValidatorTests
{
    private readonly IOptions<GoogleAuthSettings> _settings;
    private readonly TestGoogleTokenValidator _validator;

    public GoogleTokenValidatorTests()
    {
        _settings = Options.Create(new GoogleAuthSettings
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            TokenInfoEndpoint = "https://test.endpoint/tokeninfo"
        });
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GoogleTokenValidator>();

        _validator = new TestGoogleTokenValidator(
            _settings,
            new HttpClient(),  // Won't be used due to override
            logger);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        _validator.SetupTokenResponse(new GoogleTokenResponse
        {
            Aud = "test-client-id",
            Sub = "123456",
            Email = "test@example.com",
            EmailVerified = true,
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            Name = "Test User",
            Picture = "https://example.com/picture.jpg"
        });

        // Act
        var result = await _validator.ValidateIdTokenAsync("valid-token");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("123456", result.Subject);
        Assert.Equal("test@example.com", result.Email);
        Assert.Equal("Test User", result.Name);
        Assert.Equal("https://example.com/picture.jpg", result.Picture);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WithExpiredToken_ReturnsInvalid()
    {
        // Arrange
        _validator.SetupTokenResponse(new GoogleTokenResponse
        {
            Aud = "test-client-id",
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
            EmailVerified = true
        });

        // Act
        var result = await _validator.ValidateIdTokenAsync("expired-token");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Token has expired", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WithUnverifiedEmail_ReturnsInvalid()
    {
        // Arrange
        _validator.SetupTokenResponse(new GoogleTokenResponse
        {
            Aud = "test-client-id",
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            EmailVerified = false
        });

        // Act
        var result = await _validator.ValidateIdTokenAsync("unverified-email-token");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Email not verified by Google", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WithWrongAudience_ReturnsInvalid()
    {
        // Arrange
        _validator.SetupTokenResponse(new GoogleTokenResponse
        {
            Aud = "wrong-client-id",
            ExpirationTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            EmailVerified = true
        });

        // Act
        var result = await _validator.ValidateIdTokenAsync("wrong-audience-token");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Token was not issued for this application", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WithHttpError_ReturnsInvalid()
    {
        // Arrange
        _validator.SimulateHttpError();

        // Act
        var result = await _validator.ValidateIdTokenAsync("invalid-token");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Failed to connect to Google servers", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WithInvalidResponseFormat_ReturnsInvalid()
    {
        // Arrange
        _validator.SimulateInvalidResponseFormat();

        // Act
        var result = await _validator.ValidateIdTokenAsync("invalid-format-token");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Invalid token response format", result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ValidateIdTokenAsync_WithEmptyToken_ReturnsInvalid(string? token)
    {
        // Act
        var result = await _validator.ValidateIdTokenAsync(token!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("ID token is required", result.ErrorMessage);
    }
}

public class TestGoogleTokenValidator : GoogleTokenValidator
{
    private GoogleTokenResponse? _nextResponse;
    private bool _simulateHttpError;
    private bool _simulateInvalidFormat;

    public TestGoogleTokenValidator(
        IOptions<GoogleAuthSettings> settings,
        HttpClient httpClient,
        ILogger<GoogleTokenValidator> logger)
        : base(settings, httpClient, logger)
    {
    }

    public void SetupTokenResponse(GoogleTokenResponse response)
    {
        _nextResponse = response;
        _simulateHttpError = false;
        _simulateInvalidFormat = false;
    }

    public void SimulateHttpError()
    {
        _nextResponse = null;
        _simulateHttpError = true;
        _simulateInvalidFormat = false;
    }

    public void SimulateInvalidResponseFormat()
    {
        _nextResponse = null;
        _simulateHttpError = false;
        _simulateInvalidFormat = true;
    }

    protected override async Task<(bool Success, GoogleTokenResponse? Response)> GetTokenInfoFromGoogleAsync(string idToken)
    {
        await Task.Delay(1); // Simulate async operation

        if (_simulateHttpError)
        {
            throw new HttpRequestException("Simulated HTTP error");
        }

        if (_simulateInvalidFormat)
        {
            throw new JsonException("Simulated invalid format");
        }

        return _nextResponse != null 
            ? (true, _nextResponse) 
            : (false, null);
    }
}