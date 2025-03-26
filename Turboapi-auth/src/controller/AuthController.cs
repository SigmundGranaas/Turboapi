using System.Diagnostics;
using System.Security.Authentication;
using Microsoft.AspNetCore.Mvc;
using Turboapi.auth;
using Turboapi.controller;
using Turboapi.dto;
using Turboapi.services;

namespace Turboapi.Controller;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly ActivitySource _activitySource;
    private readonly AuthHelper _authHelper;

    public AuthController(
        IAuthenticationService authService,
        ILogger<AuthController> logger,
        AuthHelper authHelper)
    {
        _authService = authService;
        _logger = logger;
        _authHelper = authHelper;
        _activitySource = new ActivitySource("AuthController");
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        using var activity = _activitySource.StartActivity("Register");
        
        if (request.Password != request.ConfirmPassword)
            return BadRequest(new AuthResponse { Success = false, Error = "Passwords do not match" });

        try
        {
            var credentials = new PasswordCredentials(request.Email, request.Password) { IsRegistration = true };
            var result = await _authService.AuthenticateAsync("Password", credentials);

            if (!result.Success)
                return BadRequest(new AuthResponse { Success = false, Error = result.ErrorMessage });

            _authHelper.SetAuthCookies(Response, result.Token, result.RefreshToken);

            return Ok(new AuthResponse { 
                Success = true, 
                AccessToken = result.Token, 
                RefreshToken = result.RefreshToken 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, new AuthResponse { Success = false, Error = "An error occurred during registration" });
        }
    }
    
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        using var activity = _activitySource.StartActivity("Login");
        
        try
        {
            var credentials = new PasswordCredentials(request.Email, request.Password);
            var result = await _authService.AuthenticateAsync("Password", credentials);

            if (!result.Success)
                return Unauthorized(new AuthResponse { Success = false, Error = result.ErrorMessage });

            _authHelper.SetAuthCookies(Response, result.Token, result.RefreshToken);

            return Ok(new AuthResponse { 
                Success = true, 
                AccessToken = result.Token, 
                RefreshToken = result.RefreshToken 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new AuthResponse { Success = false, Error = "An error occurred during login" });
        }
    }

    [HttpPost("verify-token")]
    public ActionResult<ValidateAuthResponse> VerifyToken()
    {
        try
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Unauthorized(new ValidateAuthResponse { IsAuthenticated = false, Error = "No token provided" });
            }
            
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var result = _authHelper.ValidateJwtToken(token);
            
            return result.IsAuthenticated ? Ok(result) : Unauthorized(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying token");
            return StatusCode(500, new ValidateAuthResponse { IsAuthenticated = false, Error = "An error occurred" });
        }
    }

    [HttpGet("validate")]
    public async Task<ActionResult<ValidateAuthResponse>> ValidateAuthentication()
    {
        try
        {
            var (accessToken, refreshToken) = _authHelper.GetDecryptedTokens(Request);
        
            if (string.IsNullOrEmpty(accessToken))
            {
                return Unauthorized(new ValidateAuthResponse { IsAuthenticated = false, Error = "No access token found" });
            }
            
            var validationResult = _authHelper.ValidateJwtToken(accessToken);
            
            // If token is valid, return the result
            if (validationResult.IsAuthenticated)
            {
                return Ok(validationResult);
            }
            
            // If token is expired but we have refresh token, try to refresh
            if (validationResult.RequiresRefresh && !string.IsNullOrEmpty(refreshToken))
            {
                var credentials = new RefreshTokenCredentials(refreshToken);
                var result = await _authService.AuthenticateAsync("RefreshToken", credentials);

                if (result.Success)
                {
                    _authHelper.SetAuthCookies(Response, result.Token, result.RefreshToken);
                    
                    // Get new validation result from new token
                    var newValidation = _authHelper.ValidateJwtToken(result.Token);
                    return Ok(newValidation);
                }
            }
            
            // If we get here, validation failed and refresh didn't work (or wasn't attempted)
            _authHelper.ClearAuthCookies(Response);
            return Unauthorized(validationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating authentication");
            return StatusCode(500, new ValidateAuthResponse { IsAuthenticated = false, Error = "An error occurred" });
        }
    }

    [HttpGet("status")]
    public ActionResult GetAuthStatus()
    {
        var cookies = Request.Cookies.ToDictionary(c => c.Key, c => c.Value?.Length > 0);
    
        return Ok(new { 
            isAuthenticated = User.Identity?.IsAuthenticated ?? false,
            hasCookies = new {
                accessToken = cookies.ContainsKey("AccessToken"),
                refreshToken = cookies.ContainsKey("RefreshToken")
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest? request = null)
    {
        try
        {
            // Check cookie first, then request body
            var (_, cookieToken) = _authHelper.GetDecryptedTokens(Request);
            var refreshToken = cookieToken ?? request?.RefreshToken;
            
            if (string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(new AuthResponse { Success = false, Error = "No refresh token found" });
            }

            var credentials = new RefreshTokenCredentials(refreshToken);
            var result = await _authService.AuthenticateAsync("RefreshToken", credentials);

            if (!result.Success)
            {
                _authHelper.ClearAuthCookies(Response);
                return Unauthorized(new AuthResponse { Success = false, Error = result.ErrorMessage });
            }

            _authHelper.SetAuthCookies(Response, result.Token, result.RefreshToken);

            return Ok(new AuthResponse {
                Success = true,
                AccessToken = result.Token,
                RefreshToken = result.RefreshToken
            });
        }
        catch (AuthenticationException ex)
        {
            _authHelper.ClearAuthCookies(Response);
            return Unauthorized(new AuthResponse { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new AuthResponse { Success = false, Error = "An error occurred during token refresh" });
        }
    }

    [HttpPost("logout")]
    public ActionResult Logout()
    {
        _authHelper.ClearAuthCookies(Response);
        return Ok(new { Success = true });
    }
}