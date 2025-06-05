// test/Application.Tests/UseCases/Commands/RegisterUserWithPassword/RegisterUserWithPasswordCommandHandlerTests.cs
using Moq;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Contracts.V1.Tokens;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.RegisterUserWithPassword;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;
using Xunit;
using Microsoft.Extensions.Logging; 

namespace Turboapi.Application.Tests.UseCases.Commands.RegisterUserWithPassword
{
    public class RegisterUserWithPasswordCommandHandlerTests
    {
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<IPasswordHasher> _mockPasswordHasher;
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly Mock<ILogger<RegisterUserWithPasswordCommandHandler>> _mockLogger;
        private readonly RegisterUserWithPasswordCommandHandler _handler;

        public RegisterUserWithPasswordCommandHandlerTests()
        {
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockPasswordHasher = new Mock<IPasswordHasher>();
            _mockAuthTokenService = new Mock<IAuthTokenService>();
            _mockEventPublisher = new Mock<IEventPublisher>();
            _mockLogger = new Mock<ILogger<RegisterUserWithPasswordCommandHandler>>(); 

            _handler = new RegisterUserWithPasswordCommandHandler(
                _mockAccountRepository.Object,
                _mockPasswordHasher.Object,
                _mockAuthTokenService.Object,
                _mockEventPublisher.Object,
                _mockLogger.Object 
            );
        }

        [Fact]
        public async Task Handle_ShouldReturnSuccessAndTokens_WhenRegistrationIsValid()
        {
            // Arrange
            var command = new RegisterUserWithPasswordCommand("test@example.com", "Password123!");
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync((Account?)null);
            _mockPasswordHasher.Setup(h => h.HashPassword(command.Password)).Returns("hashed_password");
            
            Account? capturedAccount = null; 
            _mockAccountRepository.Setup(r => r.AddAsync(It.IsAny<Account>()))
                .Callback<Account>(acc => capturedAccount = acc)
                .Returns(Task.CompletedTask);

            _mockAuthTokenService.Setup(s => s.GenerateTokensAsync(It.IsAny<Account>()))
                .ReturnsAsync((Account acc) => new TokenResult("access_token", "refresh_token", acc.Id));

            _mockEventPublisher.Setup(p => p.PublishAsync(It.IsAny<IDomainEvent>())).Returns(Task.CompletedTask);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("access_token", result.Value.AccessToken);
            Assert.Equal("refresh_token", result.Value.RefreshToken);
            Assert.Equal(command.Email, result.Value.Email);
            
            Assert.NotNull(capturedAccount); 
            Assert.Equal(capturedAccount.Id, result.Value.AccountId); 

            _mockAccountRepository.Verify(r => r.GetByEmailAsync(command.Email), Times.Once);
            _mockPasswordHasher.Verify(h => h.HashPassword(command.Password), Times.Once);
            _mockAccountRepository.Verify(r => r.AddAsync(It.Is<Account>(acc => acc.Id == capturedAccount.Id && acc.Email == command.Email)), Times.Once);
            _mockAuthTokenService.Verify(s => s.GenerateTokensAsync(It.Is<Account>(acc => acc.Id == capturedAccount.Id)), Times.Once);
            
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e =>
                e.GetType() == typeof(AccountCreatedEvent) &&
                ((AccountCreatedEvent)e).AccountId == capturedAccount.Id)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e =>
                e.GetType() == typeof(PasswordAuthMethodAddedEvent) &&
                ((PasswordAuthMethodAddedEvent)e).AccountId == capturedAccount.Id)), Times.Once);
            
            Assert.Empty(capturedAccount.DomainEvents); 
        }

        [Fact]
        public async Task Handle_ShouldReturnEmailExistsError_WhenEmailIsTaken()
        {
            var command = new RegisterUserWithPasswordCommand("existing@example.com", "Password123!");
            var existingAccount = Account.Create(Guid.NewGuid(), command.Email, new[] { "User" }); 
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync(existingAccount);
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(RegistrationError.EmailAlreadyExists, result.Error); 
            _mockAccountRepository.Verify(r => r.GetByEmailAsync(command.Email), Times.Once);
            _mockAccountRepository.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Never);
        }
        
        [Fact]
        public async Task Handle_ShouldReturnTokenGenerationFailed_WhenTokenServiceFails()
        {
            var command = new RegisterUserWithPasswordCommand("test@example.com", "Password123!");
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync((Account?)null);
            _mockPasswordHasher.Setup(h => h.HashPassword(command.Password)).Returns("hashed_password");
            _mockAuthTokenService.Setup(s => s.GenerateTokensAsync(It.IsAny<Account>()))
                                 .ThrowsAsync(new Exception("Token service internal error"));
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(RegistrationError.TokenGenerationFailed, result.Error);
            _mockAuthTokenService.Verify(s => s.GenerateTokensAsync(It.IsAny<Account>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ShouldReturnAccountCreationFailed_WhenRepositoryAddFails()
        {
            var command = new RegisterUserWithPasswordCommand("test@example.com", "Password123!");
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync((Account?)null);
            _mockPasswordHasher.Setup(h => h.HashPassword(command.Password)).Returns("hashed_password");
            _mockAccountRepository.Setup(r => r.AddAsync(It.IsAny<Account>())).ThrowsAsync(new Exception("Database error"));
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(RegistrationError.AccountCreationFailed, result.Error);
            _mockAccountRepository.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Once);
            _mockAuthTokenService.Verify(s => s.GenerateTokensAsync(It.IsAny<Account>()), Times.Never);
        }
        
        [Fact]
        public async Task Handle_ShouldReturnEventPublishFailed_WhenEventPublisherFails()
        {
            var command = new RegisterUserWithPasswordCommand("test@example.com", "Password123!");
            Account? capturedAccountForEventTest = null;
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync((Account?)null);
            _mockPasswordHasher.Setup(h => h.HashPassword(command.Password)).Returns("hashed_password");
            _mockAccountRepository.Setup(r => r.AddAsync(It.IsAny<Account>()))
                .Callback<Account>(acc => capturedAccountForEventTest = acc) 
                .Returns(Task.CompletedTask);

            _mockAuthTokenService.Setup(s => s.GenerateTokensAsync(It.IsAny<Account>()))
                .ReturnsAsync((Account acc) => new TokenResult("access_token", "refresh_token", acc.Id));
            _mockEventPublisher.Setup(p => p.PublishAsync(It.IsAny<IDomainEvent>()))
                               .ThrowsAsync(new Exception("Kafka unavailable"));
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(RegistrationError.EventPublishFailed, result.Error);
            Assert.NotNull(capturedAccountForEventTest); 
            
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e =>
                e.GetType() == typeof(AccountCreatedEvent) &&
                ((AccountCreatedEvent)e).AccountId == capturedAccountForEventTest.Id)), Times.AtLeastOnce());
            // Depending on loop behavior, other events might not be attempted if the first one throws.
            // If the loop continues, verify others. If it breaks, Times.Never for subsequent ones.
            // For this test, AtLeastOnce for the first type of event that would be published is sufficient.

            Assert.NotEmpty(capturedAccountForEventTest.DomainEvents);
        }

        [Theory]
        [InlineData(null, "password")]
        [InlineData("", "password")]
        [InlineData(" ", "password")]
        [InlineData("email@test.com", null)]
        [InlineData("email@test.com", "")]
        [InlineData("email@test.com", " ")]
        public async Task Handle_ShouldReturnInvalidInput_ForNullOrWhitespaceEmailOrPassword(string email, string password)
        {
            var command = new RegisterUserWithPasswordCommand(email, password);
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(RegistrationError.InvalidInput, result.Error);
            _mockAccountRepository.Verify(r => r.GetByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_ShouldReturnAuthMethodCreationFailed_WhenDomainExceptionOccursDuringAccountCreation()
        {
            var command = new RegisterUserWithPasswordCommand("test@example.com", "short"); 
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync((Account?)null);
            _mockPasswordHasher.Setup(h => h.HashPassword(command.Password))
                               .Throws(new Domain.Exceptions.DomainException("Simulated domain error during hashing/auth method creation."));
            var result = await _handler.Handle(command, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(RegistrationError.AuthMethodCreationFailed, result.Error);
            _mockAccountRepository.Verify(r => r.GetByEmailAsync(command.Email), Times.Once); 
            _mockPasswordHasher.Verify(h => h.HashPassword(command.Password), Times.Once); 
            _mockAccountRepository.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Never); 
        }
    }
}