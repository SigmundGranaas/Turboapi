using System.Net;
using System.Net.Http.Headers;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Contracts.V1.Common;
using Turboapi.Application.Contracts.V1.Session;
using Turboapi.Application.Contracts.V1.Tokens;
using Turboapi.Application.UseCases.Queries.ValidateSession;
using Turboapi.Domain.Events;
using Xunit;

namespace Turboapi.Presentation.Tests.Controllers
{
    [Collection("ApiCollection")]
    public class TokenAndSessionControllerTests : IAsyncLifetime
    {
        private readonly ApiTestFixture _fixture;
        private readonly HttpClient _client;
        private readonly TestEventPublisher _eventPublisher;

        public TokenAndSessionControllerTests(ApiTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.Client;
            _eventPublisher = fixture.EventPublisher;
        }

        public async Task InitializeAsync()
        {
            await _fixture.ResetDatabaseAsync();
            _eventPublisher.Clear();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Refresh_WithValidRefreshToken_ReturnsNewTokens()
        {
            // Arrange
            var userTokens = await SetupUserAndGetTokensAsync();
            var request = new RefreshTokenRequest(userTokens.RefreshToken);
            _eventPublisher.Clear(); // Clear setup events

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/token/refresh", request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var newTokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
            Assert.NotNull(newTokens);
            Assert.NotEqual(userTokens.AccessToken, newTokens.AccessToken);
            Assert.NotEqual(userTokens.RefreshToken, newTokens.RefreshToken);
            Assert.Equal(userTokens.AccountId, newTokens.AccountId);

            Assert.Contains(_eventPublisher.PublishedEvents, e => e is RefreshTokenRevokedEvent);
            Assert.Contains(_eventPublisher.PublishedEvents, e => e is RefreshTokenGeneratedEvent);
        }

        [Fact]
        public async Task Refresh_WithInvalidRefreshToken_ReturnsUnauthorized()
        {
            // Arrange
            await SetupUserAndGetTokensAsync(); // Ensure a user exists, but we won't use their token
            var request = new RefreshTokenRequest("this-is-a-fake-token");

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/token/refresh", request);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("RefreshTokenError", error?.ErrorCode);
            Assert.Equal("InvalidToken", error?.Message);
        }

        [Fact]
        public async Task Refresh_WithUsedRefreshToken_ReturnsUnauthorized()
        {
            // Arrange
            var userTokens = await SetupUserAndGetTokensAsync();
            var firstRefreshRequest = new RefreshTokenRequest(userTokens.RefreshToken);
            var firstRefreshResponse = await _client.PostAsJsonAsync("/api/v1/token/refresh", firstRefreshRequest);
            Assert.Equal(HttpStatusCode.OK, firstRefreshResponse.StatusCode); // Ensure first refresh succeeded

            // Act
            // Attempt to use the same, now-revoked token again
            var secondRefreshResponse = await _client.PostAsJsonAsync("/api/v1/token/refresh", firstRefreshRequest);
            
            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, secondRefreshResponse.StatusCode);
            var error = await secondRefreshResponse.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("RefreshTokenError", error?.ErrorCode);
            Assert.Equal("Revoked", error?.Message);
        }
        
        [Fact]
        public async Task GetCurrentUser_WithValidAccessToken_ReturnsSessionDetails()
        {
            // Arrange
            var userTokens = await SetupUserAndGetTokensAsync();
            
            // Add the token to the request header for the [Authorize] attribute
            _client.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", userTokens.AccessToken);
            
            // Act
            // Call the new GET endpoint, no request body needed.
            var response = await _client.GetAsync("/api/v1/session/me");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var session = await response.Content.ReadFromJsonAsync<ValidateSessionResponse>();
            Assert.NotNull(session);
            Assert.Equal(userTokens.AccountId, session.AccountId);
            Assert.True(session.IsActive);
            Assert.Contains("User", session.Roles);

            // Clean up
            _client.DefaultRequestHeaders.Authorization = null;
        }
        
        [Fact]
        public async Task GetCurrentUser_WithInvalidAccessToken_ReturnsUnauthorized()
        {
            // Arrange
            _client.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", "this-is-a-bad-jwt");

            // Act
            var response = await _client.GetAsync("/api/v1/session/me");
            
            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Empty(content);

            // Clean up
            _client.DefaultRequestHeaders.Authorization = null;
        }

        private async Task<AuthTokenResponse> SetupUserAndGetTokensAsync()
        {
            var userEmail = $"user-{Guid.NewGuid()}@session.com";
            var password = "Password123!";
            
            var registerRequest = new RegisterUserWithPasswordRequest(userEmail, password, password);
            var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
            Assert.Equal(HttpStatusCode.OK, regResponse.StatusCode);

            var loginRequest = new LoginUserWithPasswordRequest(userEmail, password);
            var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            
            var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
            Assert.NotNull(tokens);
            return tokens;
        }
    }
}