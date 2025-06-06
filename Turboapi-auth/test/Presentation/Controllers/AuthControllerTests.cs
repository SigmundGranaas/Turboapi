using System.Net;
using Microsoft.EntityFrameworkCore;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Domain.Events;
using Turboapi.Infrastructure.Persistence;
using Turboapi.Presentation.Cookies;
using Xunit;

namespace Turboapi.Presentation.Tests.Controllers
{
    [Collection("ApiCollection")]
    public class AuthControllerTests : IAsyncLifetime
    {
        private readonly ApiTestFixture _fixture;
        private readonly HttpClient _client;
        private readonly TestEventPublisher _eventPublisher;

        public AuthControllerTests(ApiTestFixture fixture)
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
        public async Task Register_WithExistingEmail_ReturnsConflictAndDoesNotSetCookies()
        {
            // Arrange
            var request = new RegisterUserWithPasswordRequest("test@example.com", "Password123!", "Password123!");
            await _client.PostAsJsonAsync("/api/v1/auth/register", request);
            
            // Act
            var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
            Assert.False(secondResponse.Headers.Contains("Set-Cookie"));
        }

         private static void AssertAuthCookiesAreSet(HttpResponseMessage response)
        {
            var setCookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();
            Assert.Contains(setCookieHeaders, h => h.StartsWith(CookieManager.AccessTokenCookieName));
            Assert.Contains(setCookieHeaders, h => h.StartsWith(CookieManager.RefreshTokenCookieName));
            Assert.All(setCookieHeaders, h => Assert.Contains("httponly", h, StringComparison.OrdinalIgnoreCase));
            Assert.All(setCookieHeaders, h => Assert.Contains("samesite=Lax", h, StringComparison.OrdinalIgnoreCase));
            Assert.All(setCookieHeaders, h => Assert.Contains("path=/", h, StringComparison.OrdinalIgnoreCase));
            // FIX: Since the client is now using HTTPS, we MUST assert that the 'secure' flag is present.
            Assert.All(setCookieHeaders, h => Assert.Contains("secure", h, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Register_WithValidData_ReturnsSuccessAndSetsCookies()
        {
            // Arrange
            var request = new RegisterUserWithPasswordRequest("test@example.com", "Password123!", "Password123!");

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
            Assert.NotNull(content);
            Assert.False(string.IsNullOrWhiteSpace(content.AccessToken));
            
            AssertAuthCookiesAreSet(response);

            using var scope = _fixture.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            Assert.True(await context.Accounts.AnyAsync(a => a.Email == "test@example.com"));
            Assert.Contains(_eventPublisher.PublishedEvents, e => e is AccountCreatedEvent);
        }

        [Fact]
        public async Task Login_WithValidCredentials_ReturnsSuccessAndSetsCookies()
        {
            // Arrange
            var registerRequest = new RegisterUserWithPasswordRequest("login.valid@example.com", "Password123!", "Password123!");
            await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
            var loginRequest = new LoginUserWithPasswordRequest("login.valid@example.com", "Password123!");

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertAuthCookiesAreSet(response);
        }

        [Fact]
        public async Task Login_WithInvalidCredentials_ReturnsUnauthorizedAndDoesNotSetCookies()
        {
            // Arrange
            var registerRequest = new RegisterUserWithPasswordRequest("login.invalid@example.com", "Password123!", "Password123!");
            await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
            
            var loginRequest = new LoginUserWithPasswordRequest("login.invalid@example.com", "WrongPassword!");

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }
}