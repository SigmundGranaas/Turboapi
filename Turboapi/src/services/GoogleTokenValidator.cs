using System.Text.Json;
using Turboapi.Models;

public class GoogleTokenValidator
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleTokenValidator> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public GoogleTokenValidator(
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<GoogleTokenValidator> logger)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
        _clientId = configuration["Authentication:Google:ClientId"] 
            ?? throw new ArgumentNullException("Google ClientId is not configured");
        _clientSecret = configuration["Authentication:Google:ClientSecret"] 
            ?? throw new ArgumentNullException("Google ClientSecret is not configured");
    }

    public async Task<GoogleTokenInfo> ValidateIdTokenAsync(string idToken)
    {
        try
        {
            if (string.IsNullOrEmpty(idToken))
            {
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "ID token is required" 
                };
            }

            // Validate token with Google's OAuth2 v3 endpoint
            var response = await _httpClient.GetAsync(
                $"https://oauth2.googleapis.com/tokeninfo?id_token={idToken}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to validate Google token. Status: {Status}, Error: {Error}", 
                    response.StatusCode, error);
                
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "Invalid token" 
                };
            }

            var tokenInfo = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>();
            if (tokenInfo == null)
            {
                return new GoogleTokenInfo 
                { 
                    IsValid = false, 
                    ErrorMessage = "Failed to parse token information" 
                };
            }

            // Verify the token is intended for your application
            if (tokenInfo.Aud != _clientId)
            {
                _logger.LogWarning("Token has incorrect audience. Expected: {Expected}, Got: {Actual}", 
                    _clientId, tokenInfo.Aud);
                
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
}