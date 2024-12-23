using System.Text.Json;
using Microsoft.Extensions.Options;
using Turboapi.auth;
using Turboapi.Models;

namespace Turboapi.Services;

public class GoogleTokenValidator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleTokenValidator> _logger;
    private readonly GoogleAuthSettings _settings;

    public GoogleTokenValidator(
        IOptions<GoogleAuthSettings> settings,
        HttpClient httpClient,
        ILogger<GoogleTokenValidator> logger)
    {
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        
        if (string.IsNullOrEmpty(_settings.ClientId))
            throw new ArgumentException("Google ClientId is not configured");
        if (string.IsNullOrEmpty(_settings.ClientSecret))
            throw new ArgumentException("Google ClientSecret is not configured");
            
        _httpClient = httpClient;
        _logger = logger;
    }

    public async  Task<GoogleTokenInfo> ValidateIdTokenAsync(string? idToken)
    {
        try
        {
            if (string.IsNullOrEmpty(idToken) || string.IsNullOrWhiteSpace(idToken))
            {
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "ID token is required" 
                };
            }

            var (success, tokenInfo) = await GetTokenInfoFromGoogleAsync(idToken);
            
            if (!success || tokenInfo == null)
            {
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "Invalid token" 
                };
            }

            // Verify the token is intended for your application
            if (tokenInfo.Aud != _settings.ClientId)
            {
                _logger.LogWarning("Token has incorrect audience. Expected: {Expected}, Got: {Actual}", 
                    _settings.ClientId, tokenInfo.Aud);
                
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "Token was not issued for this application" 
                };
            }

            // Verify token hasn't expired
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now > tokenInfo.ExpirationTime)
            {
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "Token has expired" 
                };
            }

            // Verify email is verified by Google
            if (!tokenInfo.EmailVerified)
            {
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "Email not verified by Google" 
                };
            }

            return new GoogleTokenInfo
            {
                IsValid = true,
                Subject = tokenInfo.Sub,
                Email = tokenInfo.Email,
                AccessToken = idToken,
                Name = tokenInfo.Name,
                Picture = tokenInfo.Picture
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while validating Google token");
            return new GoogleTokenInfo 
            { 
                IsValid = false, 
                ErrorMessage = "Failed to connect to Google servers" 
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Google token response");
            return new GoogleTokenInfo 
            { 
                IsValid = false, 
                ErrorMessage = "Invalid token response format" 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Google token validation");
            return new GoogleTokenInfo 
            { 
                IsValid = false, 
                ErrorMessage = "An unexpected error occurred" 
            };
        }
    }

    protected virtual async Task<(bool Success, GoogleTokenResponse? Response)> GetTokenInfoFromGoogleAsync(string idToken)
    {
        var response = await _httpClient.GetAsync(
            $"{_settings.TokenInfoEndpoint}?id_token={idToken}");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to validate Google token. Status: {Status}, Error: {Error}", 
                response.StatusCode, error);
            return (false, null);
        }

        var tokenInfo = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>();
        return (true, tokenInfo);
    }
}