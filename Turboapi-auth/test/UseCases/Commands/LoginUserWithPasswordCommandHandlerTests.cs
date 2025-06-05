// test/Application.Tests/UseCases/Commands/LoginUserWithPassword/LoginUserWithPasswordCommandHandlerTests.cs
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
using Microsoft.Extensions.Logging;

namespace Turboapi.Application.Tests.UseCases.Commands.LoginUserWithPassword
{
    public class LoginUserWithPasswordCommandHandlerTests
    {
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<IPasswordHasher> _mockPasswordHasher;
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly Mock<ILogger<LoginUserWithPasswordCommandHandler>> _mockLogger;
        private readonly LoginUserWithPasswordCommandHandler _handler;

        public LoginUserWithPasswordCommandHandlerTests()
        {
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockPasswordHasher = new Mock<IPasswordHasher>();
            _mockAuthTokenService = new Mock<IAuthTokenService>();
            _mockEventPublisher = new Mock<IEventPublisher>();
            _mockLogger = new Mock<ILogger<LoginUserWithPasswordCommandHandler>>();

            _mockPasswordHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("a_standard_test_hashed_password");

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
            var accountId = Guid.NewGuid();
            var email = "test@example.com";
            var password = "Password123!";
            var command = new LoginUserWithPasswordCommand(email, password);

            var account = CreateCleanTestAccountWithPassword(accountId, email, password, _mockPasswordHasher.Object);
            var passwordAuthMethod = account.AuthenticationMethods.OfType<PasswordAuthMethod>().First(); 
            
            _mockPasswordHasher.Setup(h => h.VerifyPassword(password, "a_standard_test_hashed_password")).Returns(true);
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(email)).ReturnsAsync(account);
            
            var tokenResult = new TokenResult("access_token", "refresh_token", accountId);
            _mockAuthTokenService.Setup(s => s.GenerateTokensAsync(account)).ReturnsAsync(tokenResult);
            _mockEventPublisher.Setup(p => p.PublishAsync(It.IsAny<IDomainEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(tokenResult.AccessToken, result.Value.AccessToken);
            
            _mockAccountRepository.Verify(r => r.GetByEmailAsync(email), Times.Once);
            _mockPasswordHasher.Verify(h => h.VerifyPassword(password, "a_standard_test_hashed_password"), Times.Once);
            _mockAccountRepository.Verify(r => r.UpdateAsync(account), Times.Once); 
            _mockAuthTokenService.Verify(s => s.GenerateTokensAsync(account), Times.Once);
            
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => 
                e.GetType() == typeof(AccountLastLoginUpdatedEvent) && 
                ((AccountLastLoginUpdatedEvent)e).AccountId == accountId)), Times.Once);

            _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<AccountCreatedEvent>()), Times.Never);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<PasswordAuthMethodAddedEvent>()), Times.Never);

            Assert.NotNull(account.LastLoginAt); 
            Assert.True(passwordAuthMethod.LastUsedAt.HasValue); 
            Assert.Empty(account.DomainEvents); 
        }

        [Fact]
        public async Task Handle_ShouldReturnAccountNotFound_WhenEmailDoesNotExist()
        {
            var command = new LoginUserWithPasswordCommand("unknown@example.com", "Password123!");
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync((Account?)null);
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(LoginError.AccountNotFound, result.Error);
        }

        [Fact]
        public async Task Handle_ShouldReturnPasswordMethodNotFound_WhenAccountHasNoPasswordAuth()
        {
            var accountId = Guid.NewGuid();
            var email = "nopassword@example.com";
            var command = new LoginUserWithPasswordCommand(email, "Password123!");
            var account = Account.Create(accountId, email, new[] { "User" }); 
            account.ClearDomainEvents(); 
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(email)).ReturnsAsync(account);
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(LoginError.PasswordMethodNotFound, result.Error);
        }

        [Fact]
        public async Task Handle_ShouldReturnInvalidCredentials_WhenPasswordIsIncorrect()
        {
            var accountId = Guid.NewGuid();
            var email = "test@example.com";
            var correctPassword = "Password123!";
            var wrongPassword = "WrongPassword!";
            var command = new LoginUserWithPasswordCommand(email, wrongPassword);

            var account = CreateCleanTestAccountWithPassword(accountId, email, correctPassword, _mockPasswordHasher.Object);
            _mockPasswordHasher.Setup(h => h.VerifyPassword(wrongPassword, "a_standard_test_hashed_password")).Returns(false);
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(email)).ReturnsAsync(account);
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(LoginError.InvalidCredentials, result.Error);
            _mockPasswordHasher.Verify(h => h.VerifyPassword(wrongPassword, "a_standard_test_hashed_password"), Times.Once);
        }
        
        [Fact]
        public async Task Handle_ShouldReturnTokenGenerationFailed_WhenTokenServiceFails()
        {
            var accountId = Guid.NewGuid();
            var email = "test@example.com";
            var password = "Password123!";
            var command = new LoginUserWithPasswordCommand(email, password);

            var account = CreateCleanTestAccountWithPassword(accountId, email, password, _mockPasswordHasher.Object);
             _mockPasswordHasher.Setup(h => h.VerifyPassword(password, "a_standard_test_hashed_password")).Returns(true);
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(email)).ReturnsAsync(account);
            _mockAuthTokenService.Setup(s => s.GenerateTokensAsync(account)).ThrowsAsync(new Exception("Token service boom"));
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(LoginError.TokenGenerationFailed, result.Error);
        }
    }
}