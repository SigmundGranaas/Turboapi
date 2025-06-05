using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Domain.Aggregates;
using Turboapi.Domain.Events;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Application.UseCases.Commands.AuthenticateWithOAuth
{
    public class AuthenticateWithOAuthCommandHandler : ICommandHandler<AuthenticateWithOAuthCommand, Result<AuthTokenResponse, OAuthLoginError>>
    {
        private readonly IEnumerable<IOAuthProviderAdapter> _oauthAdapters;
        private readonly IAccountRepository _accountRepository;
        private readonly IAuthTokenService _authTokenService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<AuthenticateWithOAuthCommandHandler> _logger;
        private const bool EmailMustBeVerified = true;

        public AuthenticateWithOAuthCommandHandler(
            IEnumerable<IOAuthProviderAdapter> oauthAdapters,
            IAccountRepository accountRepository,
            IAuthTokenService authTokenService,
            IEventPublisher eventPublisher,
            ILogger<AuthenticateWithOAuthCommandHandler> logger)
        {
            _oauthAdapters = oauthAdapters;
            _accountRepository = accountRepository;
            _authTokenService = authTokenService;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<Result<AuthTokenResponse, OAuthLoginError>> Handle(
            AuthenticateWithOAuthCommand command,
            CancellationToken cancellationToken)
        {
            try
            {
                var adapter = _oauthAdapters.FirstOrDefault(a => a.ProviderName.Equals(command.ProviderName, StringComparison.OrdinalIgnoreCase));
                if (adapter == null) return OAuthLoginError.UnsupportedProvider;

                var tokenExchangeResult = await adapter.ExchangeCodeForTokensAsync(command.AuthorizationCode, command.RedirectUri);
                if (tokenExchangeResult.IsFailure) return OAuthLoginError.ProviderError;
                
                var userInfoResult = await adapter.GetUserInfoAsync(tokenExchangeResult.Value!.AccessToken);
                if (userInfoResult.IsFailure) return OAuthLoginError.ProviderError;

                var userInfo = userInfoResult.Value!;
                if (EmailMustBeVerified && !userInfo.IsEmailVerified) return OAuthLoginError.EmailNotVerified;

                var account = await _accountRepository.GetByOAuthAsync(command.ProviderName, userInfo.ExternalId) ?? await _accountRepository.GetByEmailAsync(userInfo.Email);
                
                OAuthAuthMethod? currentOAuthMethod;

                if (account == null)
                {
                    account = Account.Create(Guid.NewGuid(), userInfo.Email, new[] { "User" });
                    account.AddOAuthAuthMethod(command.ProviderName, userInfo.ExternalId, tokenExchangeResult.Value!.AccessToken, tokenExchangeResult.Value!.RefreshToken, null);
                    await _accountRepository.AddAsync(account);
                }
                else
                {
                    var oauthMethod = account.AuthenticationMethods.OfType<OAuthAuthMethod>()
                        .FirstOrDefault(m => m.ProviderName.Equals(command.ProviderName, StringComparison.OrdinalIgnoreCase));
                    
                    if (oauthMethod == null)
                    {
                        account.AddOAuthAuthMethod(command.ProviderName, userInfo.ExternalId, tokenExchangeResult.Value!.AccessToken, tokenExchangeResult.Value!.RefreshToken, null);
                    }
                    else
                    {
                        oauthMethod.UpdateTokens(tokenExchangeResult.Value!.AccessToken, tokenExchangeResult.Value!.RefreshToken, null);
                    }
                }
                
                // *** FIX: Update last login AFTER potential method creation/update ***
                account.UpdateLastLogin();
                currentOAuthMethod = account.AuthenticationMethods.OfType<OAuthAuthMethod>().First(m => m.ProviderName.Equals(command.ProviderName, StringComparison.OrdinalIgnoreCase));
                currentOAuthMethod.UpdateLastUsed();

                await _accountRepository.UpdateAsync(account);

                var tokenResult = await _authTokenService.GenerateTokensAsync(account);

                // *** FIX: Publish all domain events from the aggregate ***
                var eventsToPublish = account.DomainEvents.ToList();
                account.ClearDomainEvents();
                foreach (var domainEvent in eventsToPublish)
                {
                    await _eventPublisher.PublishAsync(domainEvent);
                }
                
                // *** FIX: Explicitly publish the application-level login event ***
                await _eventPublisher.PublishAsync(new AccountLoggedInEvent(account.Id, currentOAuthMethod.Id, command.ProviderName, DateTime.UtcNow));

                return new AuthTokenResponse(tokenResult.AccessToken, tokenResult.RefreshToken, account.Id, account.Email);
            }
            // *** FIX: Catch potential exceptions during persistence ***
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during OAuth authentication for provider {Provider}", command.ProviderName);
                return OAuthLoginError.AccountCreationFailed; // Or a more generic error
            }
        }
    }
}