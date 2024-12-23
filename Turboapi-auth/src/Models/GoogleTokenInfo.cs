namespace Turboapi.Models;

public class GoogleTokenInfo
{
    public bool IsValid { get; set; }
    public string? Subject { get; set; }
    public string? Email { get; set; }
    public string? AccessToken { get; set; }
    public string? Name { get; set; }
    public string? Picture { get; set; }
    public string? ErrorMessage { get; set; }
}