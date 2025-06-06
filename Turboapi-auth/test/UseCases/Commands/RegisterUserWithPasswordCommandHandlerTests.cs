using Microsoft.Extensions.Logging;
using Moq;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.RegisterUserWithPassword;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;
using Xunit;

namespace Turboapi.Application.Tests.UseCases.Commands.RegisterUserWithPassword
{
    public class RegisterUserWithPasswordCommandHandlerTests
    {
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<IPasswordHasher> _mockPasswordHasher;
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly Mock<ILogger<RegisterUserWithPasswordCommandHandler>> _mockLogger;
        private readonly ICommandHandler<RegisterUserWithPasswordCommand, Result<AuthTokenResponse, RegistrationError>> _handler;

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
            
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(It.IsAny<Account>()))
                .ReturnsAsync(new NewTokenStrings("access_token", "refresh_token", DateTime.UtcNow.AddDays(7)));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            
            _mockAccountRepository.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is AccountCreatedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is PasswordAuthMethodAddedEvent)), Times.Once);
        }

        [Fact]
        public async Task Handle_ShouldReturnEventPublishFailed_WhenEventPublisherFails()
        {
            // Arrange
            var command = new RegisterUserWithPasswordCommand("test@example.com", "Password123!");
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(command.Email)).ReturnsAsync((Account?)null);
            _mockPasswordHasher.Setup(h => h.HashPassword(command.Password)).Returns("hashed_password");
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(It.IsAny<Account>())).ReturnsAsync(new NewTokenStrings("a", "r", DateTime.UtcNow.AddDays(7)));
            _mockEventPublisher.Setup(p => p.PublishAsync(It.IsAny<IDomainEvent>())).ThrowsAsync(new Exception("Kafka down"));

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(RegistrationError.EventPublishFailed, result.Error);
        }
    }
}