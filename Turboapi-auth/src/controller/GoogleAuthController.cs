using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Turboapi.auth;
using Turboapi.controller;
using Turboapi.dto;
using Turboapi.services;

namespace Turboapi.Controller;

[ApiController]
[Route("api/auth/google")]
public class GoogleAuthController : ControllerBase
{
    public class GoogleLoginRequest
    {
        [Required] public string IdToken { get; set; } = string.Empty;
    }

    private readonly IAuthenticationService _authService;
    private readonly IGoogleAuthenticationService _googleAuthService;
    private readonly ILogger<GoogleAuthController> _logger;
    private readonly AuthHelper _authHelper;

    public GoogleAuthController(
        IAuthenticationService authService,
        IGoogleAuthenticationService googleAuthService,
        ILogger<GoogleAuthController> logger,
        AuthHelper authHelper)
    {
        _authService = authService;
        _googleAuthService = googleAuthService;
        _logger = logger;
        _authHelper = authHelper;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        try
        {
            // Validate the Google ID token
            var tokenInfo = await _googleAuthService.ValidateIdTokenAsync(request.IdToken);

            if (!tokenInfo.IsValid)
            {
                return Unauthorized(new AuthResponse { Success = false, Error = tokenInfo.ErrorMessage });
            }

            // Use authentication service with validated token
            var result = await _authService.AuthenticateAsync("Google", new GoogleCredentials(request.IdToken));

            if (!result.Success)
                return Unauthorized(new AuthResponse { Success = false, Error = result.ErrorMessage });

            _authHelper.SetAuthCookies(Response, result.Token, result.RefreshToken);

            return Ok(new AuthResponse
            {
                Success = true,
                AccessToken = result.Token,
                RefreshToken = result.RefreshToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Google login");
            return StatusCode(500,
                new AuthResponse { Success = false, Error = "An error occurred during Google login" });
        }
    }

    [HttpGet("url")]
    public ActionResult<string> GetGoogleLoginUrl()
    {
        return Ok(_googleAuthService.GenerateAuthUrl());
    }

    [HttpGet("callback")]
    public async Task<ActionResult> GoogleCallback([FromQuery] string code)
    {
        try
        {
            // Exchange code for tokens
            var tokenResponse = await _googleAuthService.ExchangeCodeForTokensAsync(code);

            if (!tokenResponse.Success)
                return Redirect($"/login?error={Uri.EscapeDataString(tokenResponse.ErrorMessage ?? "Unknown error")}");

            var credentials = new GoogleCredentials(tokenResponse.IdToken);
            var result = await _authService.AuthenticateAsync("Google", credentials);

            if (!result.Success)
                return Redirect($"/login?error={Uri.EscapeDataString(result.ErrorMessage ?? "Authentication failed")}");

            _authHelper.SetAuthCookies(Response, result.Token, result.RefreshToken);

            return Redirect($"/login/success");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Google callback");
            return Redirect("/login?error=An error occurred during Google login");
        }
    }
}