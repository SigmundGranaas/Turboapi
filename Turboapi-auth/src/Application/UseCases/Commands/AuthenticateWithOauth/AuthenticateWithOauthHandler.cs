// ==== FILE: /home/sigmund/development/turboapi/Turboapi/Turboapi-auth/src/Application/UseCases/Commands/AuthenticateWithOauth/AuthenticateWithOauthHandler.cs ====
using Turbo.Messaging;
using Turbo.Outbox;
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
        private readonly IOutbox _outbox;
        private readonly ILogger<AuthenticateWithOAuthCommandHandler> _logger;
        private const bool EmailMustBeVerified = true;

        public AuthenticateWithOAuthCommandHandler(
            IEnumerable<IOAuthProviderAdapter> oauthAdapters,
            IAccountRepository accountRepository,
            IAuthTokenService authTokenService,
            IOutbox outbox,
            ILogger<AuthenticateWithOAuthCommandHandler> logger)
        {
            _oauthAdapters = oauthAdapters;
            _accountRepository = accountRepository;
            _authTokenService = authTokenService;
            _outbox = outbox;
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
                
                bool isNewAccount = false;
                if (account == null)
                {
                    isNewAccount = true;
                    account = Account.Create(Guid.NewGuid(), userInfo.Email, new[] { "User" });
                    account.AddOAuthAuthMethod(command.ProviderName, userInfo.ExternalId, tokenExchangeResult.Value!.AccessToken, tokenExchangeResult.Value!.RefreshToken, null);
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
                
                account.UpdateLastLogin();
                var currentOAuthMethod = account.AuthenticationMethods.OfType<OAuthAuthMethod>().First(m => m.ProviderName.Equals(command.ProviderName, StringComparison.OrdinalIgnoreCase));
                currentOAuthMethod.UpdateLastUsed();

                var newTokens = await _authTokenService.GenerateNewTokenStringsAsync(account);
                account.AddNewRefreshToken(newTokens.RefreshTokenValue, newTokens.RefreshTokenExpiresAt);

                if (isNewAccount)
                {
                    await _accountRepository.AddAsync(account);
                }
                else
                {
                    await _accountRepository.UpdateAsync(account);
                }

                // Aggregate-emitted events are drained into the outbox by UoW.
                // The explicit login event is not on the aggregate; append it
                // directly to the same outbox so it commits in the same
                // transaction as everything else.
                var loginEvent = new AccountLoggedInEvent(
                    account.Id, currentOAuthMethod.Id, command.ProviderName, DateTime.UtcNow);
                var loginHeaders = new Dictionary<string, string> { ["aggregateId"] = account.Id.ToString() };
                await _outbox.AppendAsync(EventEnvelopeFactory.For(loginEvent, "auth", loginHeaders), cancellationToken);

                return new AuthTokenResponse(newTokens.AccessToken, newTokens.RefreshTokenValue, account.Id, account.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred during OAuth authentication for provider {Provider}", command.ProviderName);
                return OAuthLoginError.AccountCreationFailed;
            }
        }
    }
}