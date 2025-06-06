using System.Net;
using System.Net.Http.Headers;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.UseCases.Queries.ValidateSession;
using Turboapi.Presentation.Cookies;
using Xunit;

namespace Turboapi.Presentation.Tests.Controllers
{
    [Collection("ApiCollection")]
    public class TokenAndSessionControllerTests
    {
        private readonly ApiTestFixture _fixture;
        private readonly HttpClient _client;
        private readonly TestEventPublisher _eventPublisher;

        public TokenAndSessionControllerTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.Factory.CreateClient();
            _eventPublisher = fixture.EventPublisher;

            _fixture.ResetDatabaseAsync().GetAwaiter().GetResult();
            _eventPublisher.Clear();
        }

        #region Helper Methods

        private async Task<(List<string> cookies, AuthTokenResponse tokens)> SetupUserAndGetCookiesAsync()
        {
            var userEmail = $"user-{Guid.NewGuid()}@session.com";
            var password = "Password123!";

            var registerRequest = new RegisterUserWithPasswordRequest(userEmail, password, password);
            var regResponse = await _client.PostAsJsonAsync("/api/auth/Auth/register", registerRequest);
            Assert.Equal(HttpStatusCode.OK, regResponse.StatusCode);

            var loginRequest = new LoginUserWithPasswordRequest(userEmail, password);
            var loginResponse = await _client.PostAsJsonAsync("/api/auth/Auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
            Assert.NotNull(tokens);

            var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
            Assert.NotEmpty(cookies);

            return (cookies, tokens);
        }

        private static void AssertAuthCookiesAreSet(HttpResponseMessage response)
        {
            var setCookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();
            Assert.Contains(setCookieHeaders, h => h.StartsWith(CookieManager.AccessTokenCookieName));
            Assert.Contains(setCookieHeaders, h => h.StartsWith(CookieManager.RefreshTokenCookieName));
        }

        private static void AssertAuthCookiesAreCleared(HttpResponseMessage response)
        {
            var setCookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();
            Assert.Contains(setCookieHeaders, h => h.StartsWith(CookieManager.AccessTokenCookieName) && h.Contains("expires=Thu, 01 Jan 1970"));
            Assert.Contains(setCookieHeaders, h => h.StartsWith(CookieManager.RefreshTokenCookieName) && h.Contains("expires=Thu, 01 Jan 1970"));
        }
        #endregion

        [Fact]
        public async Task Refresh_WithValidTokenInCookie_ReturnsNewTokensAndSetsNewCookies()
        {
            // Arrange
            var (cookies, originalTokens) = await SetupUserAndGetCookiesAsync();
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/auth/Token/refresh");
            
            var cookieHeader = string.Join("; ", cookies.Select(c => c.Split(';')[0]));
            requestMessage.Headers.Add("Cookie", cookieHeader);
            
            // Act
            var response = await _client.SendAsync(requestMessage);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var newTokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
            Assert.NotNull(newTokens);
            Assert.NotEqual(originalTokens.RefreshToken, newTokens.RefreshToken);
            AssertAuthCookiesAreSet(response);
        }

        [Fact]
        public async Task Revoke_WithValidTokenInCookie_ClearsCookiesAndInvalidatesToken()
        {
            // Arrange
            var (cookies, _) = await SetupUserAndGetCookiesAsync();
            var cookieHeader = string.Join("; ", cookies.Select(c => c.Split(';')[0]));

            // Act: Send revoke request with the manually added cookie.
            var revokeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/Token/revoke");
            revokeRequest.Headers.Add("Cookie", cookieHeader);
            var revokeResponse = await _client.SendAsync(revokeRequest);

            // Assert: Revocation was successful and server sent headers to clear cookies.
            Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
            AssertAuthCookiesAreCleared(revokeResponse);

            // Act 2: Attempt to use the now-revoked token from the original cookie.
            var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/Token/refresh");
            refreshRequest.Headers.Add("Cookie", cookieHeader); // Send the stale, now-revoked token
            var refreshResponse = await _client.SendAsync(refreshRequest);

            // Assert 2: The server correctly identifies the token as invalid.
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
        }

        [Fact]
        public async Task GetCurrentUser_WithValidAuthCookie_ReturnsSessionDetails()
        {
            // Arrange
            var (cookies, originalTokens) = await SetupUserAndGetCookiesAsync();
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "/api/auth/Session/me");
            var cookieHeader = string.Join("; ", cookies.Select(c => c.Split(';')[0]));
            requestMessage.Headers.Add("Cookie", cookieHeader);

            // Act
            var response = await _client.SendAsync(requestMessage);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var session = await response.Content.ReadFromJsonAsync<ValidateSessionResponse>();
            Assert.NotNull(session);
            Assert.Equal(originalTokens.AccountId, session.AccountId);
        }

        [Fact]
        public async Task GetCurrentUser_WithValidBearerToken_ReturnsSessionDetails()
        {
            // Arrange: Get tokens via the cookie setup helper.
            var (_, userTokens) = await SetupUserAndGetCookiesAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userTokens.AccessToken);

            // Act
            var response = await _client.GetAsync("/api/auth/Session/me");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var session = await response.Content.ReadFromJsonAsync<ValidateSessionResponse>();
            Assert.NotNull(session);
            Assert.Equal(userTokens.AccountId, session.AccountId);

            // Clean up
            _client.DefaultRequestHeaders.Authorization = null;
        }
    }
}