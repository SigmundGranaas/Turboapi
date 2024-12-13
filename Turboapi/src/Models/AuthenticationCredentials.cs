namespace Turboapi.Models;

public interface IAuthenticationCredentials { }

public record PasswordCredentials(string Email, string Password) : IAuthenticationCredentials;
public record GoogleCredentials(string IdToken) : IAuthenticationCredentials;
