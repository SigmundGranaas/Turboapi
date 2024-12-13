namespace Turboapi.Models;

public abstract class AuthenticationMethod
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Provider { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    
    // Navigation property
    public Account Account { get; set; }
}

public class PasswordAuthentication : AuthenticationMethod
{
    public string PasswordHash { get; set; }
    public string Salt { get; set; }
}

public class OAuthAuthentication : AuthenticationMethod
{
    public string ExternalUserId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiry { get; set; }
}

public class WebAuthnAuthentication : AuthenticationMethod
{
    public string CredentialId { get; set; }
    public string PublicKey { get; set; }
    public string? DeviceName { get; set; }
}
