using Microsoft.Extensions.Logging;
using Moq;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.RefreshToken;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;
using Xunit;

namespace Turboapi.Application.Tests.UseCases.Commands.RefreshTokenTests
{
    public class RefreshTokenCommandHandlerTests
    {
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly Mock<ILogger<RefreshTokenCommandHandler>> _mockLogger;
        private readonly RefreshTokenCommandHandler _handler;

        public RefreshTokenCommandHandlerTests()
        {
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockAuthTokenService = new Mock<IAuthTokenService>();
            _mockEventPublisher = new Mock<IEventPublisher>();
            _mockLogger = new Mock<ILogger<RefreshTokenCommandHandler>>();

            // The handler no longer depends on IRefreshTokenRepository
            _handler = new RefreshTokenCommandHandler(
                _mockAccountRepository.Object,
                _mockAuthTokenService.Object,
                _mockEventPublisher.Object,
                _mockLogger.Object
            );
        }

        private Account CreateTestAccountWithToken(string token, bool isExpired = false)
        {
            var accountId = Guid.NewGuid();
            var account = Account.Create(accountId, "test@example.com", new[] { "User" });
            
            var expiresAt = isExpired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddDays(7);
            // Use the test-friendly constructor for RefreshToken to create an expired one if needed.
            var refreshToken = RefreshToken.Create(accountId, token, expiresAt, DateTime.UtcNow.AddDays(-1));
            
            // Use internal helper to add token to the aggregate for the test setup.
            account.AddRefreshToken(refreshToken);
            account.ClearDomainEvents();
            return account;
        }

        [Fact]
        public async Task Handle_WithValidToken_ShouldSucceedAndRotateToken()
        {
            // Arrange
            var command = new RefreshTokenCommand("valid-token");
            var account = CreateTestAccountWithToken(command.RefreshTokenString);

            // Correctly mock the GetByRefreshTokenAsync method
            _mockAccountRepository
                .Setup(r => r.GetByRefreshTokenAsync(command.RefreshTokenString))
                .ReturnsAsync(account);

            var newStrings = new NewTokenStrings("new-access-token", "new-refresh-token", DateTime.UtcNow.AddDays(7));
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(account)).ReturnsAsync(newStrings);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess, "The operation should have succeeded.");
            Assert.NotNull(result.Value);
            Assert.Equal(newStrings.AccessToken, result.Value.AccessToken);
            Assert.Equal(newStrings.RefreshTokenValue, result.Value.RefreshToken);
            
            _mockAccountRepository.Verify(r => r.UpdateAsync(account), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is RefreshTokenRevokedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is RefreshTokenGeneratedEvent)), Times.Once);
        }
        
        [Fact]
        public async Task Handle_WithExpiredToken_ReturnsExpiredError()
        {
            // Arrange
            var command = new RefreshTokenCommand("expired-token");
            var account = CreateTestAccountWithToken(command.RefreshTokenString, isExpired: true);

            // The repo should still find the account, as it doesn't check for expiry.
            _mockAccountRepository
                .Setup(r => r.GetByRefreshTokenAsync(command.RefreshTokenString))
                .ReturnsAsync(account);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            // The handler calls account.RotateRefreshToken, which will detect the expiry and return the correct error.
            Assert.False(result.IsSuccess);
            Assert.Equal(RefreshTokenError.Expired, result.Error);
        }

        [Fact]
        public async Task Handle_WithNonExistentToken_ReturnsInvalidTokenError()
        {
            // Arrange
            var command = new RefreshTokenCommand("non-existent-token");
            
            // Setup the mock to return null, simulating a token that doesn't exist.
            _mockAccountRepository
                .Setup(r => r.GetByRefreshTokenAsync(command.RefreshTokenString))
                .ReturnsAsync((Account?)null);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(RefreshTokenError.InvalidToken, result.Error);
            _mockAuthTokenService.Verify(s => s.GenerateNewTokenStringsAsync(It.IsAny<Account>()), Times.Never);
        }
    }
}