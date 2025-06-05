using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web; // For HttpUtility
using Turboapi.Application.Contracts.V1.OAuth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;

namespace Turboapi.Infrastructure.Auth.OAuthProviders
{
    public class GoogleOAuthAdapter : IOAuthProviderAdapter
    {
        public string ProviderName => "Google";

        private readonly HttpClient _httpClient;
        private readonly GoogleAuthSettings _settings;
        private readonly ILogger<GoogleOAuthAdapter> _logger;
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };


        public GoogleOAuthAdapter(
            IHttpClientFactory httpClientFactory,
            IOptions<GoogleAuthSettings> settings,
            ILogger<GoogleOAuthAdapter> logger)
        {
            _httpClient = httpClientFactory.CreateClient(ProviderName); // Named client if configured, else default
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                _logger.LogError("{ProviderName} ClientId or ClientSecret is not configured.", ProviderName);
                // This state will cause ConfigurationError when methods are called.
            }
        }

        public string GetAuthorizationUrl(string? state = null, params string[]? scopes)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.RedirectUri) || string.IsNullOrWhiteSpace(_settings.AuthorizationEndpoint))
            {
                _logger.LogError("{ProviderName} adapter is not properly configured for GetAuthorizationUrl (ClientId, RedirectUri, or AuthorizationEndpoint missing).", ProviderName);
                throw new InvalidOperationException($"{ProviderName} OAuth provider is not properly configured.");
            }

            var scopesToUse = scopes != null && scopes.Length > 0 ? scopes : _settings.DefaultScopes;
            var queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["client_id"] = _settings.ClientId;
            queryParams["redirect_uri"] = _settings.RedirectUri;
            queryParams["response_type"] = "code";
            queryParams["scope"] = string.Join(" ", scopesToUse);
            if (!string.IsNullOrWhiteSpace(state))
            {
                queryParams["state"] = state;
            }
            // Common Google OAuth parameters:
            queryParams["access_type"] = "offline"; // To get a refresh token
            queryParams["prompt"] = "consent";      // To ensure refresh token is returned on subsequent authorizations

            return $"{_settings.AuthorizationEndpoint}?{queryParams.ToString()}";
        }

        public async Task<Result<OAuthProviderTokens, OAuthError>> ExchangeCodeForTokensAsync(string code, string? redirectUriOverride = null)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret) || string.IsNullOrWhiteSpace(_settings.TokenEndpoint))
            {
                _logger.LogError("{ProviderName} adapter is not properly configured for ExchangeCodeForTokensAsync.", ProviderName);
                return OAuthError.ConfigurationError;
            }
            if (string.IsNullOrWhiteSpace(code))
            {
                _logger.LogWarning("Authorization code is null or empty for {ProviderName}.", ProviderName);
                return OAuthError.InvalidCode;
            }

            var effectiveRedirectUri = redirectUriOverride ?? _settings.RedirectUri;
            if (string.IsNullOrWhiteSpace(effectiveRedirectUri))
            {
                 _logger.LogError("{ProviderName} RedirectUri is not configured and no override provided for ExchangeCodeForTokensAsync.", ProviderName);
                return OAuthError.ConfigurationError;
            }

            var tokenRequestParams = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", effectiveRedirectUri },
                { "client_id", _settings.ClientId },
                { "client_secret", _settings.ClientSecret }
            };

            try
            {
                using var requestContent = new FormUrlEncodedContent(tokenRequestParams);
                var response = await _httpClient.PostAsync(_settings.TokenEndpoint, requestContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Error exchanging code for tokens with {ProviderName}. Status: {StatusCode}, Response: {ErrorResponse}",
                        ProviderName, response.StatusCode, errorContent);
                    // Attempt to parse Google specific error
                    var googleError = TryParseGoogleError(errorContent);
                    return googleError?.Error switch
                    {
                        "invalid_grant" => OAuthError.InvalidCode, // Common for expired/used code or redirect_uri mismatch
                        "invalid_client" => OAuthError.ConfigurationError,
                        _ => OAuthError.TokenExchangeFailed
                    };
                }

                var providerTokens = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(_jsonSerializerOptions);
                if (providerTokens == null || string.IsNullOrWhiteSpace(providerTokens.AccessToken))
                {
                    _logger.LogError("Failed to deserialize token response or access token missing from {ProviderName}.", ProviderName);
                    return OAuthError.TokenExchangeFailed;
                }
                
                return new OAuthProviderTokens(
                    providerTokens.AccessToken,
                    providerTokens.IdToken,
                    providerTokens.RefreshToken,
                    providerTokens.ExpiresIn,
                    providerTokens.TokenType,
                    providerTokens.Scope
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed during token exchange with {ProviderName}.", ProviderName);
                return OAuthError.NetworkError;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed during token exchange with {ProviderName}.", ProviderName);
                return OAuthError.TokenExchangeFailed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during token exchange with {ProviderName}.", ProviderName);
                return OAuthError.TokenExchangeFailed;
            }
        }

        public async Task<Result<OAuthUserInfo, OAuthError>> GetUserInfoAsync(string providerAccessToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.UserInfoEndpoint))
            {
                 _logger.LogError("{ProviderName} adapter is not properly configured for GetUserInfoAsync (UserInfoEndpoint missing).", ProviderName);
                return OAuthError.ConfigurationError;
            }
            if (string.IsNullOrWhiteSpace(providerAccessToken))
            {
                _logger.LogWarning("Provider access token is null or empty for GetUserInfoAsync with {ProviderName}.", ProviderName);
                return OAuthError.MissingRequiredToken;
            }

            try
            {
                using var requestMessage = new HttpRequestMessage(HttpMethod.Get, _settings.UserInfoEndpoint);
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerAccessToken);
                
                var response = await _httpClient.SendAsync(requestMessage);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Error fetching user info from {ProviderName}. Status: {StatusCode}, Response: {ErrorResponse}",
                        ProviderName, response.StatusCode, errorContent);
                    return OAuthError.UserInfoFailed;
                }

                var userInfo = await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(_jsonSerializerOptions);
                if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.Sub) || string.IsNullOrWhiteSpace(userInfo.Email))
                {
                    _logger.LogError("Failed to deserialize user info or essential fields (sub, email) missing from {ProviderName}.", ProviderName);
                    return OAuthError.UserInfoFailed;
                }

                // If email_verified is false and your policy requires verified emails:
                // if (!userInfo.EmailVerified.GetValueOrDefault(false)) // Assuming EmailVerified can be null
                // {
                //     _logger.LogWarning("User email '{UserEmail}' from {ProviderName} is not verified.", userInfo.Email, ProviderName);
                //     return OAuthError.EmailNotVerified;
                // }

                return new OAuthUserInfo(
                    userInfo.Sub,
                    userInfo.Email,
                    userInfo.EmailVerified.GetValueOrDefault(false),
                    userInfo.GivenName,
                    userInfo.FamilyName,
                    userInfo.Name, // Full name
                    userInfo.Picture
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed during user info retrieval from {ProviderName}.", ProviderName);
                return OAuthError.NetworkError;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed during user info retrieval from {ProviderName}.", ProviderName);
                return OAuthError.UserInfoFailed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during user info retrieval from {ProviderName}.", ProviderName);
                return OAuthError.UserInfoFailed;
            }
        }
        
        public async Task<Result<OAuthProviderTokens, OAuthError>> RefreshAccessTokenAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret) || string.IsNullOrWhiteSpace(_settings.TokenEndpoint))
            {
                _logger.LogError("{ProviderName} adapter is not properly configured for RefreshAccessTokenAsync.", ProviderName);
                return OAuthError.ConfigurationError;
            }
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                _logger.LogWarning("Refresh token is null or empty for {ProviderName}.", ProviderName);
                return OAuthError.MissingRequiredToken;
            }

            var tokenRequestParams = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "client_id", _settings.ClientId },
                { "client_secret", _settings.ClientSecret }
            };

            try
            {
                using var requestContent = new FormUrlEncodedContent(tokenRequestParams);
                var response = await _httpClient.PostAsync(_settings.TokenEndpoint, requestContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Error refreshing access token with {ProviderName}. Status: {StatusCode}, Response: {ErrorResponse}",
                        ProviderName, response.StatusCode, errorContent);
                    return OAuthError.TokenExchangeFailed; // Or a more specific error if parsable
                }

                var providerTokens = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(_jsonSerializerOptions);
                if (providerTokens == null || string.IsNullOrWhiteSpace(providerTokens.AccessToken))
                {
                    _logger.LogError("Failed to deserialize refresh token response or access token missing from {ProviderName}.", ProviderName);
                    return OAuthError.TokenExchangeFailed;
                }
                
                // Google typically does NOT return a new refresh token during a refresh_token grant.
                // The original refresh token remains valid until revoked.
                // So, providerTokens.RefreshToken will likely be null here. We should return the original one.
                return new OAuthProviderTokens(
                    providerTokens.AccessToken,
                    providerTokens.IdToken, // ID token might be returned
                    refreshToken, // Return the original refresh token as Google doesn't issue new ones on refresh
                    providerTokens.ExpiresIn,
                    providerTokens.TokenType,
                    providerTokens.Scope
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed during access token refresh with {ProviderName}.", ProviderName);
                return OAuthError.NetworkError;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during access token refresh with {ProviderName}.", ProviderName);
                return OAuthError.TokenExchangeFailed;
            }
        }

        // Helper to parse Google's error response
        private GoogleErrorResponse? TryParseGoogleError(string errorContent)
        {
            try
            {
                return JsonSerializer.Deserialize<GoogleErrorResponse>(errorContent, _jsonSerializerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // DTOs for Google-specific responses
        private class GoogleTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("id_token")]
            public string? IdToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int? ExpiresIn { get; set; }

            [JsonPropertyName("token_type")]
            public string? TokenType { get; set; }

            [JsonPropertyName("scope")]
            public string? Scope { get; set; }
        }
        
        private class GoogleErrorResponse
        {
            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("error_description")]
            public string? ErrorDescription { get; set; }
        }


        private class GoogleUserInfoResponse
        {
            [JsonPropertyName("sub")]
            public string? Sub { get; set; } // Subject - The user's unique Google ID

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("given_name")]
            public string? GivenName { get; set; }

            [JsonPropertyName("family_name")]
            public string? FamilyName { get; set; }

            [JsonPropertyName("picture")]
            public string? Picture { get; set; }

            [JsonPropertyName("email")]
            public string? Email { get; set; }

            [JsonPropertyName("email_verified")]
            public bool? EmailVerified { get; set; }

            [JsonPropertyName("locale")]
            public string? Locale { get; set; }
        }
    }
}