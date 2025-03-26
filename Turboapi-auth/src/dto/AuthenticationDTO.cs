namespace Turboapi.dto;

public record RegisterRequest(
    string Email,
    string Password,
    string ConfirmPassword);

public record LoginRequest(
    string Email,
    string Password);

public record AuthResponse
{
    public bool Success { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? Error { get; init; }
}

public record RefreshTokenRequest(
    string RefreshToken);
    
public class ValidateAuthResponse
{
    public bool IsAuthenticated { get; set; }
    public string? Email { get; set; }
    public string? UserId { get; set; }
    public string? AuthType { get; set; }
    public string? Error { get; set; }
    public bool RequiresRefresh { get; set; }
}