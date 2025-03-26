using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Turboapi.auth;

namespace Turboapi.services;

public interface IGoogleAuthenticationService
{
    string GenerateAuthUrl();
    Task<GoogleTokenResponse> ExchangeCodeForTokensAsync(string code);
    Task<GoogleTokenInfo> ValidateIdTokenAsync(string idToken);
}

public class GoogleAuthenticationService : IGoogleAuthenticationService
{
    private readonly GoogleAuthSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleAuthenticationService> _logger;

    public GoogleAuthenticationService(
        IOptions<GoogleAuthSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleAuthenticationService> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string GenerateAuthUrl()
    {
        var scopes = new[] { "openid", "email", "profile" };
        var scopeString = string.Join(" ", scopes);
        
        var queryParams = new Dictionary<string, string>
        {
            { "client_id", _settings.ClientId },
            { "redirect_uri", _settings.RedirectUri },
            { "response_type", "code" },
            { "scope", scopeString },
            { "access_type", "offline" },
            { "prompt", "consent" }  // Force showing consent screen to get refresh token
        };
        
        var queryString = string.Join("&", queryParams.Select(p => 
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            
        return $"https://accounts.google.com/o/oauth2/v2/auth?{queryString}";
    }

    public async Task<GoogleTokenResponse> ExchangeCodeForTokensAsync(string code)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", _settings.ClientId },
                { "client_secret", _settings.ClientSecret },
                { "redirect_uri", _settings.RedirectUri },
                { "grant_type", "authorization_code" }
            });
            
            var response = await client.PostAsync("https://oauth2.googleapis.com/token", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Google token exchange failed: {ErrorContent}", errorContent);
                return new GoogleTokenResponse 
                { 
                    Success = false, 
                    ErrorMessage = "Failed to exchange Google code for tokens" 
                };
            }
            
            var tokens = await response.Content.ReadFromJsonAsync<GoogleTokenResponseData>();
            
            if (tokens == null)
                return new GoogleTokenResponse 
                { 
                    Success = false, 
                    ErrorMessage = "Invalid response from Google" 
                };
                
            // Validate the ID token to get user info
            var tokenInfo = await ValidateIdTokenAsync(tokens.IdToken);
            
            if (!tokenInfo.IsValid)
                return new GoogleTokenResponse 
                { 
                    Success = false, 
                    ErrorMessage = tokenInfo.ErrorMessage 
                };
                
            return new GoogleTokenResponse
            {
                Success = true,
                IdToken = tokens.IdToken,
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                ExpiresIn = tokens.ExpiresIn,
                TokenType = tokens.TokenType,
                UserInfo = tokenInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging Google code for tokens");
            return new GoogleTokenResponse 
            { 
                Success = false, 
                ErrorMessage = "An error occurred during Google authentication" 
            };
        }
    }

    public async Task<GoogleTokenInfo> ValidateIdTokenAsync(string idToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{_settings.TokenInfoEndpoint}?id_token={idToken}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Google token validation failed: {ErrorContent}", errorContent);
                return new GoogleTokenInfo
                {
                    IsValid = false,
                    ErrorMessage = "Failed to validate Google token"
                };
            }
            
            var tokenInfo = await response.Content.ReadFromJsonAsync<GoogleTokenInfoResponse>();
            
            if (tokenInfo == null)
                return new GoogleTokenInfo { IsValid = false, ErrorMessage = "Invalid token response from Google" };
                
            // Validate audience (client ID)
            if (tokenInfo.Aud != _settings.ClientId)
                return new GoogleTokenInfo { IsValid = false, ErrorMessage = "Invalid token audience" };
                
            // Validate token expiration
            var expirationTime = DateTimeOffset.FromUnixTimeSeconds(tokenInfo.Exp).UtcDateTime;
            if (expirationTime < DateTime.UtcNow)
                return new GoogleTokenInfo { IsValid = false, ErrorMessage = "Token has expired" };
                
            return new GoogleTokenInfo
            {
                IsValid = true,
                Subject = tokenInfo.Sub,
                Email = tokenInfo.Email,
                Name = tokenInfo.Name,
                Picture = tokenInfo.Picture,
                EmailVerified = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Google token");
            return new GoogleTokenInfo { IsValid = false, ErrorMessage = "An error occurred validating the token" };
        }
    }
}

// Response classes
public class GoogleTokenResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string IdToken { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public GoogleTokenInfo? UserInfo { get; set; }
}

// Internal class for deserializing the token response
internal class GoogleTokenResponseData
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonPropertyName("id_token")]
    public string IdToken { get; set; } = string.Empty;
    
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}

// DTO for token info response
public class GoogleTokenInfoResponse
{
    [JsonPropertyName("iss")]
    public string Iss { get; set; } = string.Empty;
    
    [JsonPropertyName("sub")]
    public string Sub { get; set; } = string.Empty;
    
    [JsonPropertyName("azp")]
    public string Azp { get; set; } = string.Empty;
    
    [JsonPropertyName("aud")]
    public string Aud { get; set; } = string.Empty;
    
    [JsonPropertyName("exp")]
    public long Exp { get; set; }
    
    [JsonPropertyName("iat")]
    public long Iat { get; set; }
    
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    
    // Modified to handle string or boolean values
    [JsonPropertyName("email_verified")]
    public string EmailVerifiedString { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("picture")]
    public string Picture { get; set; } = string.Empty;
}

// Information extracted from a validated token
public class GoogleTokenInfo
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Picture { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
}