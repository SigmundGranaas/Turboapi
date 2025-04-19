using Turboapi.dto;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Turboapi.controller;

public class AuthHelper
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthHelper> _logger;
    private readonly CookieSettings _cookieSettings;
    private const string PURPOSE = "AuthCookie.v1";

    public AuthHelper(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        IOptions<CookieSettings> cookieSettings,
        ILogger<AuthHelper> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _configuration = configuration;
        _cookieSettings = cookieSettings.Value;
        _logger = logger;
    }

    private IDataProtector CreateProtector()
    {
        return _dataProtectionProvider.CreateProtector(PURPOSE);
    }

    private string EncryptToken(string token)
    {
        var protector = CreateProtector();
        return protector.Protect(token);
    }

    private string DecryptToken(string encryptedToken)
    {
        var protector = CreateProtector();
        return protector.Unprotect(encryptedToken);
    }

    public void SetAuthCookies(HttpResponse response, string accessToken, string refreshToken)
    {
        var cookieOptions = CookieHelper.CreateAuthCookieOptions(_configuration);
        var encryptedAccessToken = EncryptToken(accessToken);
        var encryptedRefreshToken = EncryptToken(refreshToken);

        response.Cookies.Append("AccessToken", encryptedAccessToken, cookieOptions);
        response.Cookies.Append("RefreshToken", encryptedRefreshToken, cookieOptions);
        
        _logger.LogInformation("Setting authentication cookies");
    }

    public void ClearAuthCookies(HttpResponse response)
    {
        // Use the same options as when setting cookies to ensure they're properly cleared
        var cookieOptions = CookieHelper.CreateAuthCookieOptions(_configuration);
        
        response.Cookies.Delete("AccessToken", cookieOptions);
        response.Cookies.Delete("RefreshToken", cookieOptions);
        
        _logger.LogInformation("Cleared authentication cookies");
    }

    public (string? accessToken, string? refreshToken) GetDecryptedTokens(HttpRequest request)
    {
        var encryptedRefreshToken = request.Cookies["RefreshToken"];
        if (string.IsNullOrEmpty(encryptedRefreshToken))
        {
            return (null, null);
        }

        try
        {
            var refreshToken = DecryptToken(encryptedRefreshToken);
            
            var encryptedAccessToken = request.Cookies["AccessToken"];
            string? accessToken = null;
            if (!string.IsNullOrEmpty(encryptedAccessToken))
            {
                accessToken = DecryptToken(encryptedAccessToken);
            }

            return (accessToken, refreshToken);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt authentication cookies");
            ClearAuthCookies(request.HttpContext.Response);
            return (null, null);
        }
    }
    
    public ValidateAuthResponse ValidateJwtToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;
                
            if (jsonToken == null)
            {
                return new ValidateAuthResponse 
                { 
                    IsAuthenticated = false,
                    Error = "Invalid token format"
                };
            }
                
            // Check if token is expired
            if (jsonToken.ValidTo < DateTime.UtcNow)
            {
                return new ValidateAuthResponse 
                { 
                    IsAuthenticated = false,
                    Error = "Token expired",
                    RequiresRefresh = true
                };
            }
                
            // Extract user info from the claims
            var userEmail = jsonToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
            var userAuthType = jsonToken.Claims.FirstOrDefault(c => c.Type == "auth_type")?.Value;
            var userSubject = jsonToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                
            return new ValidateAuthResponse
            {
                IsAuthenticated = true,
                Email = userEmail,
                AuthType = userAuthType,
                UserId = userSubject
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token");
            return new ValidateAuthResponse 
            { 
                IsAuthenticated = false,
                Error = "Invalid token"
            };
        }
    }
}