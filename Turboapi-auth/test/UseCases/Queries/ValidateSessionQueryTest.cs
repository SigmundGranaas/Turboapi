using Moq;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Queries.ValidateSession;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Interfaces;
using Xunit;

namespace Turboapi.Application.Tests.UseCases.Queries.ValidateSessionTests
{
    public class ValidateSessionQueryHandlerTests
    {
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<ILogger<ValidateSessionQueryHandler>> _mockLogger;
        private readonly ValidateSessionQueryHandler _handler;

        public ValidateSessionQueryHandlerTests()
        {
            _mockAuthTokenService = new Mock<IAuthTokenService>();
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockLogger = new Mock<ILogger<ValidateSessionQueryHandler>>();
            _handler = new ValidateSessionQueryHandler(
                _mockAuthTokenService.Object,
                _mockAccountRepository.Object,
                _mockLogger.Object);
        }

        private ClaimsPrincipal CreateClaimsPrincipal(string accountId, string email, string[] roles)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, accountId),
                new(JwtRegisteredClaimNames.Email, email)
            };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        [Fact]
        public async Task Handle_WithValidTokenAndActiveAccount_ReturnsSuccessWithUserDetails()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var email = "test@example.com";
            var roles = new[] { "User", "Admin" };
            var query = new ValidateSessionQuery("valid-token");

            var claimsPrincipal = CreateClaimsPrincipal(accountId.ToString(), email, roles);
            _mockAuthTokenService.Setup(s => s.ValidateAccessTokenAsync(query.AccessToken)).ReturnsAsync(claimsPrincipal);

            var account = Account.Create(accountId, email, roles);
            _mockAccountRepository.Setup(r => r.GetByIdAsync(accountId)).ReturnsAsync(account);

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            var response = result.Value;
            Assert.NotNull(response);
            Assert.Equal(accountId, response.AccountId);
            Assert.Equal(email, response.Email);
            Assert.Equal(roles.OrderBy(r => r), response.Roles.OrderBy(r => r));
            Assert.True(response.IsActive);
        }

        [Fact]
        public async Task Handle_WithInvalidToken_ReturnsTokenInvalidError()
        {
            // Arrange
            var query = new ValidateSessionQuery("invalid-token");
            _mockAuthTokenService.Setup(s => s.ValidateAccessTokenAsync(query.AccessToken)).ReturnsAsync((ClaimsPrincipal?)null);

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(SessionValidationError.TokenInvalid, result.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public async Task Handle_WithEmptyToken_ReturnsTokenInvalidError(string token)
        {
            // Arrange
            var query = new ValidateSessionQuery(token);

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(SessionValidationError.TokenInvalid, result.Error);
            _mockAuthTokenService.Verify(s => s.ValidateAccessTokenAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_WithValidTokenButAccountNotFound_ReturnsUserNotFoundError()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var query = new ValidateSessionQuery("valid-token-for-deleted-user");

            var claimsPrincipal = CreateClaimsPrincipal(accountId.ToString(), "deleted@example.com", new[] { "User" });
            _mockAuthTokenService.Setup(s => s.ValidateAccessTokenAsync(query.AccessToken)).ReturnsAsync(claimsPrincipal);
            _mockAccountRepository.Setup(r => r.GetByIdAsync(accountId)).ReturnsAsync((Account?)null);

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(SessionValidationError.UserNotFound, result.Error);
        }

        [Fact]
        public async Task Handle_WithValidTokenButInactiveAccount_ReturnsAccountInactiveError()
        {
            // Arrange
            var accountId = Guid.NewGuid();
            var email = "inactive@example.com";
            var roles = new[] { "User" };
            var query = new ValidateSessionQuery("valid-token-for-inactive-user");

            var claimsPrincipal = CreateClaimsPrincipal(accountId.ToString(), email, roles);
            _mockAuthTokenService.Setup(s => s.ValidateAccessTokenAsync(query.AccessToken)).ReturnsAsync(claimsPrincipal);

            var account = Account.Create(accountId, email, roles);
            account.Deactivate(); // Deactivate the account
            _mockAccountRepository.Setup(r => r.GetByIdAsync(accountId)).ReturnsAsync(account);

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(SessionValidationError.AccountInactive, result.Error);
        }

        [Fact]
        public async Task Handle_WithTokenContainingInvalidGuid_ReturnsTokenInvalidError()
        {
            // Arrange
            var query = new ValidateSessionQuery("valid-token-bad-guid");
            var claimsPrincipal = CreateClaimsPrincipal("not-a-guid", "bad@guid.com", new[] { "User" });
            _mockAuthTokenService.Setup(s => s.ValidateAccessTokenAsync(query.AccessToken)).ReturnsAsync(claimsPrincipal);

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(SessionValidationError.TokenInvalid, result.Error);
            _mockAccountRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }
    }
}