namespace Turboapi.auth;

public interface IAuthenticationProvider
{
    string Name { get; }
    Task<AuthResult> AuthenticateAsync(IAuthenticationCredentials credentials);
    Task<AuthResult> RegisterAsync(string email, string password);
}