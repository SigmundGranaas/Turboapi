using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Application.UseCases.Commands.LoginUserWithPassword
{
    public class LoginUserWithPasswordCommandHandler
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
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _authTokenService = authTokenService ?? throw new ArgumentNullException(nameof(authTokenService));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Result<AuthTokenResponse, LoginError>> Handle(
            LoginUserWithPasswordCommand request,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Attempting to login user with email: {Email}", request.Email);

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                 _logger.LogWarning("Login failed due to invalid input for email: {Email}", request.Email);
                return LoginError.InvalidInput;
            }

            // 1. Fetch Account by Email
            var account = await _accountRepository.GetByEmailAsync(request.Email);
            if (account == null)
            {
                _logger.LogWarning("Login failed: Account not found for email {Email}.", request.Email);
                return LoginError.AccountNotFound;
            }

            // 2. Find Password Authentication Method
            var passwordAuthMethod = account.AuthenticationMethods
                .OfType<PasswordAuthMethod>()
                .FirstOrDefault();

            if (passwordAuthMethod == null)
            {
                _logger.LogWarning("Login failed: Account {AccountId} does not have a password authentication method.", account.Id);
                return LoginError.PasswordMethodNotFound;
            }

            // 3. Verify Password
            if (!_passwordHasher.VerifyPassword(request.Password, passwordAuthMethod.PasswordHash))
            {
                _logger.LogWarning("Login failed: Invalid password for account {AccountId}.", account.Id);
                return LoginError.InvalidCredentials; // More specific than AuthMethodVerificationFailed generally
            }

            // 4. Update Timestamps (LastLogin for Account, LastUsed for AuthMethod)
            account.UpdateLastLogin();
            passwordAuthMethod.UpdateLastUsed();
            // Note: These changes need to be persisted.

            try
            {
                await _accountRepository.UpdateAsync(account); // This should save Account and its owned entities if EF Core is configured.
                // If AuthMethod is not considered "owned" in a way that UpdateAsync cascades, it might need separate update.
                // However, typical Clean Arch/DDD has aggregate handling its children.
                // For now, assume UpdateAsync on Account covers child entity changes.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update account/auth method timestamps for account {AccountId} during login.", account.Id);
                // Decide if this is a critical failure. For login, maybe not, but data is inconsistent.
                // For now, proceed but log error. Could return a specific error if this persistence is critical.
            }


            // 5. Generate Tokens
            Contracts.V1.Tokens.TokenResult tokenResult;
            try
            {
                tokenResult = await _authTokenService.GenerateTokensAsync(account);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate tokens for account {AccountId} during login.", account.Id);
                return LoginError.TokenGenerationFailed;
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
                _logger.LogError(ex, "Failed to publish domain events for account {AccountId} after login.", account.Id);
                return LoginError.EventPublishFailed;
            }

            _logger.LogInformation("User {Email} (Account {AccountId}) logged in successfully.", request.Email, account.Id);

            // 7. Return Success Response
            return new AuthTokenResponse(
                tokenResult.AccessToken,
                tokenResult.RefreshToken,
                account.Id,
                account.Email
            );
        }
    }
}