namespace Turboapi.controller;

using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System;

public static class CookieHelper
{
    public static CookieOptions CreateAuthCookieOptions(IConfiguration configuration)
    {
        var cookieSettings = new CookieSettings();
        
        // Try to bind from configuration first
        configuration.GetSection("Cookie").Bind(cookieSettings);
        
        // Override with environment variables if present
        cookieSettings.Domain = Environment.GetEnvironmentVariable("COOKIE_DOMAIN") ?? cookieSettings.Domain;
        cookieSettings.SameSite = Environment.GetEnvironmentVariable("COOKIE_SAME_SITE") ?? cookieSettings.SameSite;
        cookieSettings.Secure = ParseBoolEnvVar("COOKIE_SECURE", cookieSettings.Secure);
        cookieSettings.ExpiryDays = ParseIntEnvVar("COOKIE_EXPIRY_DAYS", cookieSettings.ExpiryDays);
        cookieSettings.Path = Environment.GetEnvironmentVariable("COOKIE_PATH") ?? cookieSettings.Path;
        cookieSettings.UseAdditionalEncryption = ParseBoolEnvVar("COOKIE_USE_ADDITIONAL_ENCRYPTION", cookieSettings.UseAdditionalEncryption);

        // Parse SameSite mode
        SameSiteMode sameSiteMode = Enum.TryParse<SameSiteMode>(cookieSettings.SameSite, true, out var mode)
            ? mode
            : SameSiteMode.Lax; // Default to Lax instead of None for better security

        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = sameSiteMode,
            Secure = cookieSettings.Secure,
            Path = cookieSettings.Path,
            Domain = cookieSettings.Domain, // Will be null if not specified, which means current domain
            Expires = DateTime.UtcNow.AddDays(cookieSettings.ExpiryDays)
        };
    }

    // Helper method to parse boolean environment variables
    private static bool ParseBoolEnvVar(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    // Helper method to parse integer environment variables
    private static int ParseIntEnvVar(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return int.TryParse(value, out var result) ? result : defaultValue;
    }
}