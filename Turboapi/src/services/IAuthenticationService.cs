using Turboapi.auth;

namespace Turboapi.services;

public interface IAuthenticationService
{
    Task<AuthResult> AuthenticateAsync(string provider, IAuthenticationCredentials credentials);
    Task<AuthResult> LinkAuthenticationMethodAsync(Guid accountId, string provider, IAuthenticationCredentials credentials);
    Task<bool> RemoveAuthenticationMethodAsync(Guid accountId, Guid authMethodId);
}