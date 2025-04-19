namespace Turboapi.controller;


public class CookieSettings
{
    // Domain for the cookie - default null means the current domain
    public string? Domain { get; set; } = null;
    
    // SameSite attribute (None, Lax, Strict)
    public string SameSite { get; set; } = "Lax";
    
    // Whether the cookie should only be sent over HTTPS
    public bool Secure { get; set; } = true;
    
    // Expiration period in days
    public int ExpiryDays { get; set; } = 7;
    
    // Path where cookie is valid
    public string Path { get; set; } = "/";
    
    // Whether to use additional encryption beyond ASP.NET DataProtection
    public bool UseAdditionalEncryption { get; set; } = false;
    
    public static bool ParseBoolEnvVar(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    public static int ParseIntEnvVar(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return int.TryParse(value, out var result) ? result : defaultValue;
    }
}

