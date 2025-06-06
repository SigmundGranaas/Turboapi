using Microsoft.Extensions.Logging;
using Moq;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.UseCases.Commands.RevokeRefreshToken;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;
using Xunit;

namespace Turboapi.Application.Tests.UseCases.Commands
{
    public class RevokeRefreshTokenCommandHandlerTests
    {
        private readonly Mock<IRefreshTokenRepository> _mockRefreshTokenRepository;
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly RevokeRefreshTokenCommandHandler _handler;

        public RevokeRefreshTokenCommandHandlerTests()
        {
            _mockRefreshTokenRepository = new Mock<IRefreshTokenRepository>();
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockEventPublisher = new Mock<IEventPublisher>();
            var mockLogger = new Mock<ILogger<RevokeRefreshTokenCommandHandler>>();

            _handler = new RevokeRefreshTokenCommandHandler(
                _mockRefreshTokenRepository.Object,
                _mockAccountRepository.Object,
                _mockEventPublisher.Object,
                mockLogger.Object
            );
        }

        [Fact]
        public async Task Handle_WithValidToken_ShouldRevokeTokenAndReturnSuccess()
        {
            // Arrange
            var command = new RevokeRefreshTokenCommand("valid-token");
            var accountId = Guid.NewGuid();

            var refreshToken = RefreshToken.Create(accountId, command.RefreshToken, DateTime.UtcNow.AddDays(1));
            var account = Account.Create(accountId, "test@example.com", new[] { "User" });
            account.AddRefreshToken(refreshToken);
            account.ClearDomainEvents();

            _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(command.RefreshToken)).ReturnsAsync(refreshToken);
            _mockAccountRepository.Setup(r => r.GetByIdAsync(accountId)).ReturnsAsync(account);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);

            // --- THIS IS THE CORRECTED VERIFICATION ---
            // Verify that PublishAsync<IDomainEvent> was called with an argument
            // whose runtime type IS a RefreshTokenRevokedEvent.
            _mockEventPublisher.Verify(p => p.PublishAsync(
                It.Is<IDomainEvent>(e => e is RefreshTokenRevokedEvent)
            ), Times.Once);
            
            // The handler now uses the Account to revoke, so we expect a call to UpdateAsync on AccountRepository
            // (which will be handled by the UnitOfWork decorator in the real app)
            // But in this unit test, we can check that the account's state was changed.
            var tokenInAccount = account.RefreshTokens.First(rt => rt.Token == command.RefreshToken);
            Assert.True(tokenInAccount.IsRevoked);
        }

        [Fact]
        public async Task Handle_WithInvalidToken_ShouldReturnSuccessWithoutError()
        {
            // Arrange
            var command = new RevokeRefreshTokenCommand("invalid-token");
            _mockRefreshTokenRepository.Setup(r => r.GetByTokenAsync(command.RefreshToken)).ReturnsAsync((RefreshToken?)null);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            _mockAccountRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<IDomainEvent>()), Times.Never);
        }
    }
}