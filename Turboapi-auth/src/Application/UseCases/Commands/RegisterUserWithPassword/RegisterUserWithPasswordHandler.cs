using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Application.UseCases.Commands.RegisterUserWithPassword
{
    public class RegisterUserWithPasswordCommandHandler
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
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _authTokenService = authTokenService ?? throw new ArgumentNullException(nameof(authTokenService));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Method signature remains the same as what IRequestHandler would generate,
        // but without explicit interface implementation.
        public async Task<Result<AuthTokenResponse, RegistrationError>> Handle(
            RegisterUserWithPasswordCommand request,
            CancellationToken cancellationToken) // CancellationToken is still useful
        {
            _logger.LogInformation("Attempting to register user with email: {Email}", request.Email);

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                _logger.LogWarning("Registration failed due to invalid input for email: {Email}", request.Email);
                return RegistrationError.InvalidInput;
            }

            var existingAccount = await _accountRepository.GetByEmailAsync(request.Email);
            if (existingAccount != null)
            {
                _logger.LogWarning("Registration failed: Email {Email} already exists.", request.Email);
                return RegistrationError.EmailAlreadyExists;
            }

            Account account;
            try
            {
                account = Account.Create(Guid.NewGuid(), request.Email, new[] { "User" });
                account.AddPasswordAuthMethod(request.Password, _passwordHasher);
            }
            catch (Domain.Exceptions.DomainException ex)
            {
                _logger.LogWarning(ex, "Domain validation failed during account or auth method creation for email {Email}.", request.Email);
                return RegistrationError.AuthMethodCreationFailed;
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Unexpected error during account/auth method object creation for email {Email}.", request.Email);
                return RegistrationError.AccountCreationFailed;
            }

            try
            {
                await _accountRepository.AddAsync(account);
                // SaveChanges would be called by a UoW or Presentation layer component
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist new account for email {Email}.", request.Email);
                return RegistrationError.AccountCreationFailed;
            }

            Contracts.V1.Tokens.TokenResult tokenResult;
            try
            {
                tokenResult = await _authTokenService.GenerateTokensAsync(account);
                 // If GenerateTokensAsync also persists its refresh token, a SaveChanges would be needed after this
                 // if not handled by a broader UoW.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate tokens for newly registered account {AccountId}.", account.Id);
                return RegistrationError.TokenGenerationFailed;
            }
            
            try
            {
                
                foreach (var domainEvent in account.DomainEvents.ToList())
                {
                    await _eventPublisher.PublishAsync(domainEvent);
                }
                account.ClearDomainEvents(); 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish domain events for account {AccountId} after registration.", account.Id);
                return RegistrationError.EventPublishFailed;
            }

            _logger.LogInformation("User {Email} registered successfully with AccountId {AccountId}.", request.Email, account.Id);
            
            return new AuthTokenResponse(
                tokenResult.AccessToken,
                tokenResult.RefreshToken,
                account.Id,
                account.Email
            );
        }
    }
}