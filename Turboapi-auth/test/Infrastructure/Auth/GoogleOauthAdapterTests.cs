// test/Infrastructure/Auth/OAuthProviders/GoogleOAuthAdapterTests.cs
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RichardSzalay.MockHttp;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web; // For HttpUtility
using Turboapi.Application.Contracts.V1.OAuth;
using Turboapi.Application.Results.Errors;
using Turboapi.Infrastructure.Auth.OAuthProviders;
using Xunit;

namespace Turboapi.Infrastructure.Tests.Auth.OAuthProviders
{
    public class GoogleOAuthAdapterTests
    {
        private readonly Mock<ILogger<GoogleOAuthAdapter>> _mockLogger;
        private readonly GoogleAuthSettings _defaultSettings;

        public GoogleOAuthAdapterTests()
        {
            _mockLogger = new Mock<ILogger<GoogleOAuthAdapter>>();
            _defaultSettings = new GoogleAuthSettings
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                RedirectUri = "http://localhost/callback",
                AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                TokenEndpoint = "https://oauth2.googleapis.com/token",
                UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo",
                DefaultScopes = new[] { "openid", "email", "profile" }
            };
        }

        private GoogleOAuthAdapter CreateAdapter(MockHttpMessageHandler mockHttp, GoogleAuthSettings? settings = null)
        {
            var options = Options.Create(settings ?? _defaultSettings);
            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock.Setup(_ => _.CreateClient(It.IsAny<string>()))
                                 .Returns(mockHttp.ToHttpClient());

            return new GoogleOAuthAdapter(httpClientFactoryMock.Object, options, _mockLogger.Object);
        }

        [Fact]
        public void GetAuthorizationUrl_ConstructsCorrectUrl()
        {
            var adapter = CreateAdapter(new MockHttpMessageHandler()); 
            var state = "test_state";
            var scopes = new[] { "custom_scope" };

            var urlString = adapter.GetAuthorizationUrl(state, scopes);
            var uri = new Uri(urlString);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);

            Assert.Equal(_defaultSettings.AuthorizationEndpoint, uri.GetLeftPart(UriPartial.Path));
            Assert.Equal(_defaultSettings.ClientId, queryParams["client_id"]);
            Assert.Equal(_defaultSettings.RedirectUri, queryParams["redirect_uri"]);
            Assert.Equal("code", queryParams["response_type"]);
            Assert.Equal(string.Join(" ", scopes), queryParams["scope"]);
            Assert.Equal(state, queryParams["state"]);
            Assert.Equal("offline", queryParams["access_type"]);
            Assert.Equal("consent", queryParams["prompt"]);
        }
        
        [Fact]
        public void GetAuthorizationUrl_UsesDefaultScopes_WhenNoneProvided()
        {
            var adapter = CreateAdapter(new MockHttpMessageHandler());
            var urlString = adapter.GetAuthorizationUrl();
            var uri = new Uri(urlString);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            
            Assert.Equal(string.Join(" ", _defaultSettings.DefaultScopes), queryParams["scope"]);
        }

        [Fact]
        public async Task ExchangeCodeForTokensAsync_Success_ReturnsTokens()
        {
            var mockHttp = new MockHttpMessageHandler();
            var expectedTokens = new
            {
                access_token = "test_access_token",
                id_token = "test_id_token",
                refresh_token = "test_refresh_token",
                expires_in = 3600,
                token_type = "Bearer",
                scope = "openid email profile"
            };
            mockHttp.When(HttpMethod.Post, _defaultSettings.TokenEndpoint)
                    .Respond("application/json", JsonSerializer.Serialize(expectedTokens));

            var adapter = CreateAdapter(mockHttp);
            var result = await adapter.ExchangeCodeForTokensAsync("auth_code");

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(expectedTokens.access_token, result.Value.AccessToken);
            Assert.Equal(expectedTokens.id_token, result.Value.IdToken);
            Assert.Equal(expectedTokens.refresh_token, result.Value.RefreshToken);
        }

        [Fact]
        public async Task ExchangeCodeForTokensAsync_ApiError_ReturnsCorrectOAuthError()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When(HttpMethod.Post, _defaultSettings.TokenEndpoint)
                    .Respond(HttpStatusCode.BadRequest, "application/json", "{\"error\":\"invalid_grant\"}");

            var adapter = CreateAdapter(mockHttp);
            var result = await adapter.ExchangeCodeForTokensAsync("invalid_code");

            Assert.False(result.IsSuccess);
            Assert.Equal(OAuthError.InvalidCode, result.Error); 
        }
        
        [Fact]
        public async Task ExchangeCodeForTokensAsync_NetworkError_ReturnsNetworkError()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When(HttpMethod.Post, _defaultSettings.TokenEndpoint)
                    .Throw(new HttpRequestException("Network issue"));
            
            var adapter = CreateAdapter(mockHttp);
            var result = await adapter.ExchangeCodeForTokensAsync("any_code");

            Assert.False(result.IsSuccess);
            Assert.Equal(OAuthError.NetworkError, result.Error);
        }

        [Fact]
        public async Task GetUserInfoAsync_Success_ReturnsUserInfo()
        {
            var mockHttp = new MockHttpMessageHandler();
            var expectedUserInfo = new
            {
                sub = "user_123",
                email = "test@example.com",
                email_verified = true,
                given_name = "Test",
                family_name = "User",
                name = "Test User",
                picture = "http://example.com/pic.jpg"
            };
            mockHttp.When(HttpMethod.Get, _defaultSettings.UserInfoEndpoint)
                    .WithHeaders("Authorization", "Bearer test_access_token")
                    .Respond("application/json", JsonSerializer.Serialize(expectedUserInfo));

            var adapter = CreateAdapter(mockHttp);
            var result = await adapter.GetUserInfoAsync("test_access_token");

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(expectedUserInfo.sub, result.Value.ExternalId);
            Assert.Equal(expectedUserInfo.email, result.Value.Email);
            Assert.Equal(expectedUserInfo.email_verified, result.Value.IsEmailVerified);
        }

        [Fact]
        public async Task GetUserInfoAsync_ApiError_ReturnsUserInfoFailed()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When(HttpMethod.Get, _defaultSettings.UserInfoEndpoint)
                    .Respond(HttpStatusCode.Unauthorized);

            var adapter = CreateAdapter(mockHttp);
            var result = await adapter.GetUserInfoAsync("invalid_token");

            Assert.False(result.IsSuccess);
            Assert.Equal(OAuthError.UserInfoFailed, result.Error);
        }
        
        [Fact]
        public async Task RefreshAccessTokenAsync_Success_ReturnsNewTokens()
        {
            var mockHttp = new MockHttpMessageHandler();
            var originalRefreshToken = "original_refresh_token";
            var expectedResponse = new
            {
                access_token = "new_access_token",
                id_token = "new_id_token", 
                expires_in = 3599,
                token_type = "Bearer",
                scope = "openid email profile"
            };
            mockHttp.When(HttpMethod.Post, _defaultSettings.TokenEndpoint)
                .With(request => 
                    request.Content is FormUrlEncodedContent content &&
                    content.ReadAsStringAsync().Result.Contains("grant_type=refresh_token") &&
                    content.ReadAsStringAsync().Result.Contains($"refresh_token={Uri.EscapeDataString(originalRefreshToken)}"))
                .Respond("application/json", JsonSerializer.Serialize(expectedResponse));

            var adapter = CreateAdapter(mockHttp);
            var result = await adapter.RefreshAccessTokenAsync(originalRefreshToken);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(expectedResponse.access_token, result.Value.AccessToken);
            Assert.Equal(expectedResponse.id_token, result.Value.IdToken);
            Assert.Equal(originalRefreshToken, result.Value.RefreshToken); 
            Assert.Equal(expectedResponse.expires_in, result.Value.ExpiresInSeconds);
        }

        [Fact]
        public async Task RefreshAccessTokenAsync_ApiError_ReturnsTokenExchangeFailed()
        {
            var mockHttp = new MockHttpMessageHandler();
            mockHttp.When(HttpMethod.Post, _defaultSettings.TokenEndpoint)
                .Respond(HttpStatusCode.BadRequest, "application/json", "{\"error\":\"invalid_grant\"}"); 

            var adapter = CreateAdapter(mockHttp);
            var result = await adapter.RefreshAccessTokenAsync("invalid_or_revoked_refresh_token");

            Assert.False(result.IsSuccess);
            Assert.Equal(OAuthError.TokenExchangeFailed, result.Error);
        }
    }
}