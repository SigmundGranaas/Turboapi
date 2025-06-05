using Microsoft.Extensions.Logging;
using Moq;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Contracts.V1.Tokens;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.LoginUserWithPassword;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;
using Xunit;

namespace Turboapi.Application.Tests.UseCases.Commands.LoginUserWithPassword
{
    public class LoginUserWithPasswordCommandHandlerTests
    {
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<IPasswordHasher> _mockPasswordHasher;
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly Mock<ILogger<LoginUserWithPasswordCommandHandler>> _mockLogger;
        private readonly ICommandHandler<LoginUserWithPasswordCommand, Result<AuthTokenResponse, LoginError>> _handler;

        public LoginUserWithPasswordCommandHandlerTests()
        {
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockPasswordHasher = new Mock<IPasswordHasher>();
            _mockAuthTokenService = new Mock<IAuthTokenService>();
            _mockEventPublisher = new Mock<IEventPublisher>();
            _mockLogger = new Mock<ILogger<LoginUserWithPasswordCommandHandler>>();
            
            _handler = new LoginUserWithPasswordCommandHandler(
                _mockAccountRepository.Object,
                _mockPasswordHasher.Object,
                _mockAuthTokenService.Object,
                _mockEventPublisher.Object,
                _mockLogger.Object
            );
        }

        private Account CreateCleanTestAccountWithPassword(Guid accountId, string email, string password, IPasswordHasher hasher)
        {
            var account = Account.Create(accountId, email, new[] { "User" });
            account.AddPasswordAuthMethod(password, hasher);
            account.ClearDomainEvents(); 
            return account;
        }

        [Fact]
        public async Task Handle_ShouldReturnSuccessAndTokens_WhenCredentialsAreValid()
        {
            // Arrange
            var password = "Password123!";
            var command = new LoginUserWithPasswordCommand("test@example.com", password);
            var expectedHash = "hashed_password_for_valid_test";

            // *** FIX: Setup HashPassword before creating the account ***
            _mockPasswordHasher.Setup(h => h.HashPassword(password)).Returns(expectedHash);
            
            var account = CreateCleanTestAccountWithPassword(Guid.NewGuid(), command.Email, password, _mockPasswordHasher.Object);
            
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync(account);
            _mockPasswordHasher.Setup(h => h.VerifyPassword(password, expectedHash)).Returns(true);
            _mockAuthTokenService.Setup(s => s.GenerateTokensAsync(account)).ReturnsAsync(new TokenResult("access_token", "refresh_token", account.Id));
            
            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            _mockAccountRepository.Verify(r => r.UpdateAsync(account), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is AccountLastLoginUpdatedEvent)), Times.Once);
        }
        
        [Fact]
        public async Task Handle_ShouldReturnEventPublishFailed_WhenPublisherThrows()
        {
            // Arrange
            var password = "Password123!";
            var command = new LoginUserWithPasswordCommand("test@example.com", password);
            var expectedHash = "hashed_password_for_fail_test";
            
            // *** FIX: Setup HashPassword before creating the account ***
            _mockPasswordHasher.Setup(h => h.HashPassword(password)).Returns(expectedHash);

            var account = CreateCleanTestAccountWithPassword(Guid.NewGuid(), command.Email, password, _mockPasswordHasher.Object);

            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync(account);
            _mockPasswordHasher.Setup(h => h.VerifyPassword(password, expectedHash)).Returns(true);
            _mockAuthTokenService.Setup(s => s.GenerateTokensAsync(account)).ReturnsAsync(new TokenResult("a", "r", account.Id));
            _mockEventPublisher.Setup(p => p.PublishAsync(It.IsAny<IDomainEvent>())).ThrowsAsync(new Exception("Kafka down"));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(LoginError.EventPublishFailed, result.Error);
        }
    }
}