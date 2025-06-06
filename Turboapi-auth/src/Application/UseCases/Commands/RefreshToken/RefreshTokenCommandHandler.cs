using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Application.UseCases.Commands.RefreshToken
{
    public class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, Result<AuthTokenResponse, RefreshTokenError>>
    {
        private readonly IAccountRepository _accountRepository;
        private readonly IAuthTokenService _authTokenService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<RefreshTokenCommandHandler> _logger;

        public RefreshTokenCommandHandler(
            IAccountRepository accountRepository,
            IAuthTokenService authTokenService,
            IEventPublisher eventPublisher,
            ILogger<RefreshTokenCommandHandler> logger)
        {
            _accountRepository = accountRepository;
            _authTokenService = authTokenService;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<Result<AuthTokenResponse, RefreshTokenError>> Handle(
            RefreshTokenCommand command,
            CancellationToken cancellationToken)
        {
            var account = await _accountRepository.GetByRefreshTokenAsync(command.RefreshTokenString);
            
            if (account == null)
            {
                // To prevent token scanning, we don't differentiate between not found, revoked, or expired.
                return RefreshTokenError.InvalidToken;
            }

            var newGeneratedTokenStrings = await _authTokenService.GenerateNewTokenStringsAsync(account);

            var domainRotationResult = account.RotateRefreshToken(
                command.RefreshTokenString,
                newGeneratedTokenStrings.RefreshTokenValue,
                newGeneratedTokenStrings.RefreshTokenExpiresAt
            );

            if (domainRotationResult.IsFailure)
            {
                // This will catch if the token was found but was expired, and handle domain logic errors.
                return domainRotationResult.Error;
            }

            await _accountRepository.UpdateAsync(account);
            
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
                 _logger.LogError(ex, "Failed to publish domain events for account {AccountId} after token refresh.", account.Id);
                 // Non-critical, so we continue
            }

            return new AuthTokenResponse(
                newGeneratedTokenStrings.AccessToken,
                newGeneratedTokenStrings.RefreshTokenValue,
                account.Id,
                account.Email
            );
        }
    }
}