using Microsoft.AspNetCore.Mvc;
using Turboapi.auth;
using Turboapi.dto;
using Turboapi.services;
using AuthResponse = Turboapi.dto.AuthResponse;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthenticationService authService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request)
    {
        if (request.Password != request.ConfirmPassword)
            return BadRequest(new AuthResponse 
            { 
                Success = false, 
                Error = "Passwords do not match" 
            });

        try
        {
            // Use AuthenticateAsync with registration credentials
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
        [FromBody] RefreshTokenRequest request)
    {
        try
        {
            // Create refresh token credentials
            var credentials = new RefreshTokenCredentials(request.RefreshToken);
            var result = await _authService.AuthenticateAsync("RefreshToken", credentials);

            if (!result.Success)
                return Unauthorized(new AuthResponse 
                { 
                    Success = false, 
                    Error = result.ErrorMessage 
                });

            return Ok(new AuthResponse
            {
                Success = true,
                AccessToken = result.Token,
                RefreshToken = result.RefreshToken
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
}