using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Application.UseCases.Commands.LoginUserWithPassword
{
    public class LoginUserWithPasswordCommandHandler : ICommandHandler<LoginUserWithPasswordCommand, Result<AuthTokenResponse, LoginError>>
    {
        private readonly IAccountRepository _accountRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAuthTokenService _authTokenService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<LoginUserWithPasswordCommandHandler> _logger;

        public LoginUserWithPasswordCommandHandler(
            IAccountRepository accountRepository,
            IPasswordHasher passwordHasher,
            IAuthTokenService authTokenService,
            IEventPublisher eventPublisher,
            ILogger<LoginUserWithPasswordCommandHandler> logger)
        {
            _accountRepository = accountRepository;
            _passwordHasher = passwordHasher;
            _authTokenService = authTokenService;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<Result<AuthTokenResponse, LoginError>> Handle(
            LoginUserWithPasswordCommand command,
            CancellationToken cancellationToken)
        {
            var account = await _accountRepository.GetByEmailAsync(command.Email);
            if (account == null) return LoginError.AccountNotFound;

            var passwordAuthMethod = account.AuthenticationMethods.OfType<PasswordAuthMethod>().FirstOrDefault();
            if (passwordAuthMethod == null) return LoginError.PasswordMethodNotFound;

            if (!_passwordHasher.VerifyPassword(command.Password, passwordAuthMethod.PasswordHash))
            {
                return LoginError.InvalidCredentials;
            }

            account.UpdateLastLogin();
            passwordAuthMethod.UpdateLastUsed();

            await _accountRepository.UpdateAsync(account);
            
            var tokenResult = await _authTokenService.GenerateTokensAsync(account);

            try
            {
                var eventsToPublish = account.DomainEvents.ToList();
                account.ClearDomainEvents();
                foreach (var domainEvent in eventsToPublish)
                {
                    await _eventPublisher.PublishAsync(domainEvent);
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Failed to publish domain events for account {AccountId} after login.", account.Id);
                 return LoginError.EventPublishFailed;
            }

            return new AuthTokenResponse(
                tokenResult.AccessToken,
                tokenResult.RefreshToken,
                account.Id,
                account.Email
            );
        }
    }
}