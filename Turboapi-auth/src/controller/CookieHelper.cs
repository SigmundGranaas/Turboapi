namespace Turboapi.controller;

using Microsoft.Extensions.Configuration;
using System;

public class CookieSettings
{
    public string Domain { get; set; } = "localhost";
    public string SameSite { get; set; } = "None";
    public bool Secure { get; set; } = true;
    public int ExpiryDays { get; set; } = 7;
}

public static class CookieHelper
{
    public static CookieOptions CreateAuthCookieOptions(IConfiguration configuration)
    {
        var cookieSettings = new CookieSettings();
        configuration.GetSection("Cookie").Bind(cookieSettings);

        SameSiteMode sameSiteMode = Enum.TryParse<SameSiteMode>(cookieSettings.SameSite, true, out var mode)
            ? mode
            : SameSiteMode.None;

        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = sameSiteMode,
            Secure = cookieSettings.Secure,
            Path = "/",
            Domain = cookieSettings.Domain,
            Expires = DateTime.UtcNow.AddDays(cookieSettings.ExpiryDays)
        };
    }
}
