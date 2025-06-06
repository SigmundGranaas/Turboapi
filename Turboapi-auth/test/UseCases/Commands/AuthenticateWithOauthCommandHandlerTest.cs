using Microsoft.Extensions.Logging;
using Moq;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Contracts.V1.OAuth;
using Turboapi.Application.Contracts.V1.Tokens;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.AuthenticateWithOAuth;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;
using Xunit;

namespace Turboapi.Application.Tests.UseCases.Commands.AuthenticateWithOAuth
{
    public class AuthenticateWithOAuthCommandHandlerTests
    {
        private readonly Mock<IAccountRepository> _mockAccountRepository;
        private readonly Mock<IAuthTokenService> _mockAuthTokenService;
        private readonly Mock<IEventPublisher> _mockEventPublisher;
        private readonly Mock<ILogger<AuthenticateWithOAuthCommandHandler>> _mockLogger;
        private readonly Mock<IOAuthProviderAdapter> _mockOAuthAdapter;
        private readonly AuthenticateWithOAuthCommandHandler _handler;
        private readonly string _defaultProviderName = "TestGoogle";

        public AuthenticateWithOAuthCommandHandlerTests()
        {
            _mockAccountRepository = new Mock<IAccountRepository>();
            _mockAuthTokenService = new Mock<IAuthTokenService>();
            _mockEventPublisher = new Mock<IEventPublisher>();
            _mockLogger = new Mock<ILogger<AuthenticateWithOAuthCommandHandler>>();
            _mockOAuthAdapter = new Mock<IOAuthProviderAdapter>();
            _mockOAuthAdapter.Setup(a => a.ProviderName).Returns(_defaultProviderName);

            var adapters = new List<IOAuthProviderAdapter> { _mockOAuthAdapter.Object };

            _handler = new AuthenticateWithOAuthCommandHandler(
                adapters,
                _mockAccountRepository.Object,
                _mockAuthTokenService.Object,
                _mockEventPublisher.Object,
                _mockLogger.Object
            );
        }
        
        private AuthenticateWithOAuthCommand CreateCommand(string code = "auth_code", string? providerName = null)
        {
            return new AuthenticateWithOAuthCommand(providerName ?? _defaultProviderName, code, null, null);
        }

        private OAuthProviderTokens CreateSampleProviderTokens() => new("prov_access_token", "prov_id_token", "prov_refresh_token", 3600, "Bearer", "openid email profile");
        private OAuthUserInfo CreateSampleUserInfo(string externalId = "ext_id_123", string email = "oauth@example.com") => new(externalId, email, true, "OAuth", "User", "OAuth User", "http://pic.com/oauth.jpg");

        [Fact]
        public async Task Handle_NewUser_RegistersAndLogsInSuccessfully()
        {
            var command = CreateCommand();
            var providerTokens = CreateSampleProviderTokens();
            var userInfo = CreateSampleUserInfo();
            Account? capturedAccount = null;

            _mockOAuthAdapter.Setup(a => a.ExchangeCodeForTokensAsync(command.AuthorizationCode, command.RedirectUri)).ReturnsAsync(providerTokens);
            _mockOAuthAdapter.Setup(a => a.GetUserInfoAsync(providerTokens.AccessToken)).ReturnsAsync(userInfo);
            _mockAccountRepository.Setup(r => r.GetByOAuthAsync(_defaultProviderName, userInfo.ExternalId)).ReturnsAsync((Account?)null);
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(userInfo.Email)).ReturnsAsync((Account?)null);
            _mockAccountRepository.Setup(r => r.AddAsync(It.IsAny<Account>())).Callback<Account>(acc => capturedAccount = acc);
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(It.IsAny<Account>())).ReturnsAsync(new NewTokenStrings("sys_access_token", "sys_refresh_token", DateTime.UtcNow.AddDays(7)));

            var result = await _handler.Handle(command, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(capturedAccount);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is AccountCreatedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is OAuthAuthMethodAddedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is AccountLastLoginUpdatedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<AccountLoggedInEvent>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ExistingOAuthUser_LogsInSuccessfully()
        {
            var command = CreateCommand();
            var providerTokens = CreateSampleProviderTokens();
            var userInfo = CreateSampleUserInfo();
            var existingAccount = Account.Create(Guid.NewGuid(), userInfo.Email, new[] { "User" });
            existingAccount.AddOAuthAuthMethod(_defaultProviderName, userInfo.ExternalId);
            existingAccount.ClearDomainEvents();

            _mockOAuthAdapter.Setup(a => a.ExchangeCodeForTokensAsync(command.AuthorizationCode, command.RedirectUri)).ReturnsAsync(providerTokens);
            _mockOAuthAdapter.Setup(a => a.GetUserInfoAsync(providerTokens.AccessToken)).ReturnsAsync(userInfo);
            _mockAccountRepository.Setup(r => r.GetByOAuthAsync(_defaultProviderName, userInfo.ExternalId)).ReturnsAsync(existingAccount);
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(existingAccount)).ReturnsAsync(new NewTokenStrings("sys_access_token", "sys_refresh_token", DateTime.UtcNow.AddDays(7)));

            var result = await _handler.Handle(command, CancellationToken.None);

            Assert.True(result.IsSuccess);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is AccountLastLoginUpdatedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<AccountLoggedInEvent>()), Times.Once);
        }

        [Fact]
        public async Task Handle_ExistingEmailUser_LinksOAuthAndLogsInSuccessfully()
        {
            var command = CreateCommand();
            var providerTokens = CreateSampleProviderTokens();
            var userInfo = CreateSampleUserInfo();
            var existingAccount = Account.Create(Guid.NewGuid(), userInfo.Email, new[] { "User" }); 
            existingAccount.ClearDomainEvents();

            _mockOAuthAdapter.Setup(a => a.ExchangeCodeForTokensAsync(command.AuthorizationCode, command.RedirectUri)).ReturnsAsync(providerTokens);
            _mockOAuthAdapter.Setup(a => a.GetUserInfoAsync(providerTokens.AccessToken)).ReturnsAsync(userInfo);
            _mockAccountRepository.Setup(r => r.GetByOAuthAsync(_defaultProviderName, userInfo.ExternalId)).ReturnsAsync((Account?)null);
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(userInfo.Email)).ReturnsAsync(existingAccount);
            _mockAuthTokenService.Setup(s => s.GenerateNewTokenStringsAsync(existingAccount)).ReturnsAsync(new NewTokenStrings("sys_access_token", "sys_refresh_token", DateTime.UtcNow.AddDays(7)));

            var result = await _handler.Handle(command, CancellationToken.None);

            Assert.True(result.IsSuccess);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is OAuthAuthMethodAddedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.Is<IDomainEvent>(e => e is AccountLastLoginUpdatedEvent)), Times.Once);
            _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<AccountLoggedInEvent>()), Times.Once);
        }

        [Fact]
        public async Task Handle_AccountAddFails_ReturnsAccountCreationFailedError()
        {
            var command = CreateCommand();
            _mockOAuthAdapter.Setup(a => a.ExchangeCodeForTokensAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(CreateSampleProviderTokens());
            _mockOAuthAdapter.Setup(a => a.GetUserInfoAsync(It.IsAny<string>())).ReturnsAsync(CreateSampleUserInfo());
            _mockAccountRepository.Setup(r => r.GetByOAuthAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((Account?)null);
            _mockAccountRepository.Setup(r => r.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((Account?)null);
            _mockAccountRepository.Setup(r => r.AddAsync(It.IsAny<Account>())).ThrowsAsync(new Exception("DB Add failed"));

            var result = await _handler.Handle(command, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(OAuthLoginError.AccountCreationFailed, result.Error);
        }
        
        [Fact]
        public async Task Handle_UnsupportedProvider_ReturnsCorrectError()
        {
            var command = CreateCommand(providerName: "UnsupportedProvider");
            
            var result = await _handler.Handle(command, CancellationToken.None);
            
            Assert.False(result.IsSuccess);
            Assert.Equal(OAuthLoginError.UnsupportedProvider, result.Error); 
        }
    }
}