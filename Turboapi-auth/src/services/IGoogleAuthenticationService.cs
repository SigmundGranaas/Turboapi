using Turboapi.auth;

namespace Turboapi.services;

public interface IGoogleAuthenticationService
{
    Task<AuthResult> ExchangeCodeForTokensAsync(string code);
    string GenerateAuthUrl();
}