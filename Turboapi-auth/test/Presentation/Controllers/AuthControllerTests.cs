using System.Net;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Domain.Events;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Turboapi.Infrastructure.Persistence;

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
        public async Task Register_WithValidData_ReturnsSuccessAndTokens()
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
            
            // Verify DB state
            using var scope = _fixture.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var accountExists = await context.Accounts.AnyAsync(a => a.Email == "test@example.com");
            Assert.True(accountExists, "Account should exist in the database after successful registration.");

            Assert.Contains(_eventPublisher.PublishedEvents, e => e is AccountCreatedEvent);
        }

        [Fact]
        public async Task Register_WithExistingEmail_ReturnsConflict()
        {
            // Arrange
            var request = new RegisterUserWithPasswordRequest("test@example.com", "Password123!", "Password123!");
            var firstResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            
            // Act
            var secondResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        }

        [Fact]
        public async Task Login_WithValidCredentials_ReturnsSuccessAndTokens()
        {
            // Arrange
            var registerRequest = new RegisterUserWithPasswordRequest("login.valid@example.com", "Password123!", "Password123!");
            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
            Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

            var loginRequest = new LoginUserWithPasswordRequest("login.valid@example.com", "Password123!");

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
        {
            // Arrange
            var registerRequest = new RegisterUserWithPasswordRequest("login.invalid@example.com", "Password123!", "Password123!");
            var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
            Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
            
            var loginRequest = new LoginUserWithPasswordRequest("login.invalid@example.com", "WrongPassword!");

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        
        [Fact]
        public async Task Login_WithNonExistentUser_ReturnsNotFound()
        {
            // Arrange
            var loginRequest = new LoginUserWithPasswordRequest("nosuchuser@example.com", "anypassword");

            // Act
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}