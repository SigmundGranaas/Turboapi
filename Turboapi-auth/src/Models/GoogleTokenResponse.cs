using System.Text.Json.Serialization;

namespace Turboapi.Models;

public class GoogleTokenResponse
{
    [JsonPropertyName("aud")]
    public string Aud { get; set; }

    [JsonPropertyName("sub")]
    public string Sub { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("email_verified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("picture")]
    public string Picture { get; set; }

    [JsonPropertyName("given_name")]
    public string GivenName { get; set; }

    [JsonPropertyName("family_name")]
    public string FamilyName { get; set; }

    [JsonPropertyName("locale")]
    public string Locale { get; set; }

    [JsonPropertyName("iat")]
    public long IssuedAt { get; set; }

    [JsonPropertyName("exp")]
    public long ExpirationTime { get; set; }
}