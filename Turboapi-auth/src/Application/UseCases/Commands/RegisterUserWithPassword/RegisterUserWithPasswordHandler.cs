using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces; // Implement ICommandHandler
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Application.UseCases.Commands.RegisterUserWithPassword
{
    // Implement the ICommandHandler interface
    public class RegisterUserWithPasswordCommandHandler : ICommandHandler<RegisterUserWithPasswordCommand, Result<AuthTokenResponse, RegistrationError>>
    {
        private readonly IAccountRepository _accountRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAuthTokenService _authTokenService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<RegisterUserWithPasswordCommandHandler> _logger;

        public RegisterUserWithPasswordCommandHandler(
            IAccountRepository accountRepository,
            IPasswordHasher passwordHasher,
            IAuthTokenService authTokenService,
            IEventPublisher eventPublisher,
            ILogger<RegisterUserWithPasswordCommandHandler> logger)
        {
            _accountRepository = accountRepository;
            _passwordHasher = passwordHasher;
            _authTokenService = authTokenService;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<Result<AuthTokenResponse, RegistrationError>> Handle(
            RegisterUserWithPasswordCommand command,
            CancellationToken cancellationToken)
        {
            if (await _accountRepository.GetByEmailAsync(command.Email) != null)
            {
                return RegistrationError.EmailAlreadyExists;
            }

            var account = Account.Create(Guid.NewGuid(), command.Email, new[] { "User" });
            account.AddPasswordAuthMethod(command.Password, _passwordHasher);
            
            await _accountRepository.AddAsync(account);
            
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
                _logger.LogError(ex, "Failed to publish domain events for account {AccountId} after registration.", account.Id);
                return RegistrationError.EventPublishFailed;
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