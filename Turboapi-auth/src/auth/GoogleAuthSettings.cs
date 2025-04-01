namespace Turboapi.auth;

public class GoogleAuthSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string TokenInfoEndpoint { get; set; } = "https://oauth2.googleapis.com/tokeninfo";
}