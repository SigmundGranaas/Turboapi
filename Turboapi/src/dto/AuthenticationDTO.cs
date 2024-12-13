namespace Turboapi.dto;

public record RegisterRequest(
    string Email,
    string Password,
    string ConfirmPassword);

public record LoginRequest(
    string Email,
    string Password);

public record GoogleLoginRequest(
    string IdToken);

public class GoogleAuthConfig
{
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string RedirectUri { get; set; }
}

public record AuthResponse
{
    public bool Success { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? Error { get; init; }
}

public record RefreshTokenRequest(
    string RefreshToken);