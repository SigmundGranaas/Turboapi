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
        private readonly Mock<IRefreshTokenRepository> _mockRefreshTokenRepository;
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly Mock<ILogger<RefreshTokenCommandHandler>> _mockLogger;
        private readonly ICommandHandler<RefreshTokenCommand, Result<AuthTokenResponse, RefreshTokenError>> _handler;

        public RefreshTokenCommandHandlerTests()
        {
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockRefreshTokenRepository = new Mock<IRefreshTokenRepository>();
            _mockAuthTokenService = new Mock<IAuthTokenService>();
            _mockEventPublisher = new Mock<IEventPublisher>();
            _mockLogger = new Mock<ILogger<RefreshTokenCommandHandler>>();

            _handler = new RefreshTokenCommandHandler(
                _mockAccountRepository.Object,
                _mockRefreshTokenRepository.Object,
                _mockAuthTokenService.Object,
                _mockEventPublisher.Object,
                _mockLogger.Object
            );
        }

        private RefreshToken CreateTestRefreshToken(Guid accountId, string token, bool isRevoked = false, bool isExpired = false)
        {
            var expiresAt = isExpired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddDays(7);
            var createdAt = isExpired ? DateTime.UtcNow.AddDays(-8) : DateTime.UtcNow.AddDays(-1);
            var refreshToken = RefreshToken.Create(accountId, token, expiresAt, createdAt);
            if (isRevoked)
            {
                refreshToken.Revoke("Test Revocation");
            }
            return refreshToken;
        }

        [Fact]
        public async Task Handle_WithValidToken_ShouldSucceedAndRotateToken()
        {
            // Arrange
            var command = new RefreshTokenCommand("valid-token");
            var accountId = Guid.NewGuid();

            var oldRefreshToken = CreateTestRefreshToken(accountId, command.RefreshTokenString);
            var account = Account.Create(accountId, "test@example.com", new[] { "User" });
            account.AddRefreshToken(oldRefreshToken);
            account.ClearDomainEvents();

            _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(command.RefreshTokenString)).ReturnsAsync(oldRefreshToken);
            _mockAccountRepository.Setup(r => r.GetByIdAsync(accountId)).ReturnsAsync(account);

            var newStrings = new NewTokenStrings("new-access-token", "new-refresh-token", DateTime.UtcNow.AddDays(7));
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(account)).ReturnsAsync(newStrings);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            
            _mockAccountRepository.Verify(r => r.UpdateAsync(account), Times.Once);

            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is RefreshTokenRevokedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is RefreshTokenGeneratedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is AccountLastLoginUpdatedEvent)), Times.Once);
        }
        
        [Fact]
        public async Task Handle_WithExpiredToken_ReturnsExpiredError()
        {
            // Arrange
            var command = new RefreshTokenCommand("expired-token");
            var expiredToken = CreateTestRefreshToken(Guid.NewGuid(), command.RefreshTokenString, isExpired: true);
            _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(command.RefreshTokenString)).ReturnsAsync(expiredToken);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(RefreshTokenError.Expired, result.Error);
        }

        [Fact]
        public async Task Handle_WithValidTokenButMissingAccount_ReturnsAccountNotFoundError()
        {
            // Arrange
            var command = new RefreshTokenCommand("valid-token-no-account");
            var refreshToken = CreateTestRefreshToken(Guid.NewGuid(), command.RefreshTokenString);
            _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(command.RefreshTokenString)).ReturnsAsync(refreshToken);
            _mockAccountRepository.Setup(r => r.GetByIdAsync(refreshToken.AccountId)).ReturnsAsync((Account?)null);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(RefreshTokenError.AccountNotFound, result.Error);
        }
    }
}