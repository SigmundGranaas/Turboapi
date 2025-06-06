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

            _handler = new RefreshTokenCommandHandler(
                _mockAccountRepository.Object,
                _mockAuthTokenService.Object,
                _mockEventPublisher.Object,
                _mockLogger.Object
            );
        }

        // Helper to create an Account aggregate with a specific refresh token for testing.
        // This helper uses the internal 'AddRefreshToken' method on the Account, which
        // assumes 'InternalsVisibleTo' is configured for the test project.
        private Account CreateTestAccountWithToken(string token, bool isExpired = false)
        {
            var accountId = Guid.NewGuid();
            var account = Account.Create(accountId, "test@example.com", new[] { "User" });
            
            var expiresAt = isExpired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddDays(7);
            
            // Use the test-friendly factory to create a token that can be expired.
            var refreshToken = RefreshToken.Create(accountId, token, expiresAt, DateTime.UtcNow.AddDays(-1));
            
            // This is the key part of the setup: ensuring the token is part of the aggregate's state.
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

            _mockAccountRepository
                .Setup(r => r.GetByRefreshTokenAsync(command.RefreshTokenString))
                .ReturnsAsync(account);

            var newStrings = new NewTokenStrings("new-access-token", "new-refresh-token", DateTime.UtcNow.AddDays(7));
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(account)).ReturnsAsync(newStrings);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess, "The operation with a valid token should have succeeded.");
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
            var accountWithExpiredToken = CreateTestAccountWithToken(command.RefreshTokenString, isExpired: true);

            _mockAccountRepository
                .Setup(r => r.GetByRefreshTokenAsync(command.RefreshTokenString))
                .ReturnsAsync(accountWithExpiredToken);

            // FIX: Add the missing mock setup for GenerateNewTokenStringsAsync.
            // The handler calls this method BEFORE it checks the domain logic for expiry.
            _mockAuthTokenService
                .Setup(s => s.GenerateNewTokenStringsAsync(It.IsAny<Account>()))
                .ReturnsAsync(new NewTokenStrings("any-access-token", "any-refresh-token", DateTime.UtcNow.AddDays(7)));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(RefreshTokenError.Expired, result.Error);
        }

        [Fact]
        public async Task Handle_WithNonExistentToken_ReturnsInvalidTokenError()
        {
            // Arrange
            var command = new RefreshTokenCommand("non-existent-token");
            
            // The repository will return null for a token that doesn't belong to any account.
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