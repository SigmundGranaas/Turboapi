namespace Turboapi.auth;

public interface IAuthenticationCredentials { }

public class PasswordCredentials : IAuthenticationCredentials
{
    public string Email { get; set; }
    public string Password { get; set; }
    public bool IsRegistration { get; set; }

    public PasswordCredentials(string email, string password)
    {
        Email = email;
        Password = password;
        IsRegistration = false;
    }
}

public class RefreshTokenCredentials : IAuthenticationCredentials
{
    public string RefreshToken { get; set; }

    public RefreshTokenCredentials(string refreshToken)
    {
        RefreshToken = refreshToken;
    }
}

public record GoogleCredentials : IAuthenticationCredentials
{
    public string IdToken { get; init; }
    public string? AccessToken { get; init; }

    public GoogleCredentials(string idToken)
    {
        IdToken = idToken;
        AccessToken = null;
    }
}
public record AuthResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? AccountId { get; init; }
    public string? Token { get; init; }
    public string? RefreshToken { get; init; }
}