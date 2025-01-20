using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Turboapi.auth;
using Turboapi.dto;
using Turboapi.services;
using AuthResponse = Turboapi.dto.AuthResponse;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ActivitySource _activitySource;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private const int COOKIE_EXPIRY_DAYS = 7;
    private const string PURPOSE = "AuthCookie.v1";

    public AuthController(
        IAuthenticationService authService,
        ILogger<AuthController> logger,
        IDataProtectionProvider dataProtectionProvider)
    {
        _authService = authService;
        _logger = logger;
        _activitySource = new ActivitySource("AuthController");
        _dataProtectionProvider = dataProtectionProvider;
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

    private void SetAuthenticationCookies(string accessToken, string refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddDays(COOKIE_EXPIRY_DAYS)
        };

        // Encrypt tokens before storing in cookies
        var encryptedAccessToken = EncryptToken(accessToken);
        var encryptedRefreshToken = EncryptToken(refreshToken);

        Response.Cookies.Append("AccessToken", encryptedAccessToken, cookieOptions);
        Response.Cookies.Append("RefreshToken", encryptedRefreshToken, cookieOptions);
    }

    private void ClearAuthenticationCookies()
    {
        Response.Cookies.Delete("AccessToken");
        Response.Cookies.Delete("RefreshToken");
    }

    private (string? accessToken, string? refreshToken) GetDecryptedTokens()
    {
        var encryptedRefreshToken = Request.Cookies["RefreshToken"];
        if (string.IsNullOrEmpty(encryptedRefreshToken))
        {
            return (null, null);
        }

        try
        {
            var refreshToken = DecryptToken(encryptedRefreshToken);
            
            var encryptedAccessToken = Request.Cookies["AccessToken"];
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
            ClearAuthenticationCookies();
            return (null, null);
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request)
    {
        using var activity = _activitySource.StartActivity("Register");
        activity?.SetTag("auth.register", "New registration");
        
        if (request.Password != request.ConfirmPassword)
            return BadRequest(new AuthResponse 
            { 
                Success = false, 
                Error = "Passwords do not match" 
            });

        try
        {
            var credentials = new PasswordCredentials(request.Email, request.Password)
            {
                IsRegistration = true
            };

            var result = await _authService.AuthenticateAsync("Password", credentials);

            if (!result.Success)
                return BadRequest(new AuthResponse 
                { 
                    Success = false, 
                    Error = result.ErrorMessage 
                });

            // Set encrypted cookies
            SetAuthenticationCookies(result.Token, result.RefreshToken);

            return Ok(new AuthResponse
            {
                Success = true,
                AccessToken = result.Token,
                RefreshToken = result.RefreshToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, new AuthResponse 
            { 
                Success = false, 
                Error = "An error occurred during registration" 
            });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request)
    {
        using var activity = _activitySource.StartActivity("Login");
        activity?.SetTag("auth.login", "New login");
        try
        {
            var credentials = new PasswordCredentials(request.Email, request.Password);
            var result = await _authService.AuthenticateAsync("Password", credentials);

            if (!result.Success)
                return Unauthorized(new AuthResponse 
                { 
                    Success = false, 
                    Error = result.ErrorMessage 
                });

            // Set encrypted cookies
            SetAuthenticationCookies(result.Token, result.RefreshToken);

            return Ok(new AuthResponse
            {
                Success = true,
                AccessToken = result.Token,
                RefreshToken = result.RefreshToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new AuthResponse 
            { 
                Success = false, 
                Error = "An error occurred during login" 
            });
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> RefreshToken(
        [FromBody] RefreshTokenRequest? request = null)
    {
        try
        {
            string? refreshToken = null;
            
            // Always check cookie first, regardless of body content
            var (_, cookieToken) = GetDecryptedTokens();
            
            refreshToken = cookieToken ?? request?.RefreshToken;
            
            if (string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(new AuthResponse 
                { 
                    Success = false, 
                    Error = "No refresh token found in cookies or request body" 
                });
            }

            var credentials = new RefreshTokenCredentials(refreshToken);
            var result = await _authService.AuthenticateAsync("RefreshToken", credentials);

            if (!result.Success)
            {
                ClearAuthenticationCookies();
                return Unauthorized(new AuthResponse 
                { 
                    Success = false, 
                    Error = result.ErrorMessage 
                });
            }

            // Update cookies with new encrypted tokens
            SetAuthenticationCookies(result.Token, result.RefreshToken);

            return Ok(new AuthResponse
            {
                Success = true,
                AccessToken = result.Token,
                RefreshToken = result.RefreshToken
            });
        }
        catch (AuthenticationException ex)
        {
            ClearAuthenticationCookies();
            return Unauthorized(new AuthResponse 
            { 
                Success = false,
                Error = ex.Message 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new AuthResponse 
            { 
                Success = false, 
                Error = "An error occurred during token refresh" 
            });
        }
    }

    [HttpPost("logout")]
    public ActionResult Logout()
    {
        ClearAuthenticationCookies();
        return Ok(new { Success = true });
    }
}