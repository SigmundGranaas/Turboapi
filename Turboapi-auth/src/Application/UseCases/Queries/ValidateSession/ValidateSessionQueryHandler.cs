using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Application.UseCases.Queries.ValidateSession
{
    public class ValidateSessionQueryHandler
    {
        private readonly IAuthTokenService _authTokenService;
        private readonly IAccountRepository _accountRepository;
        private readonly ILogger<ValidateSessionQueryHandler> _logger;

        public ValidateSessionQueryHandler(
            IAuthTokenService authTokenService,
            IAccountRepository accountRepository,
            ILogger<ValidateSessionQueryHandler> logger)
        {
            _authTokenService = authTokenService;
            _accountRepository = accountRepository;
            _logger = logger;
        }

        public async Task<Result<ValidateSessionResponse, SessionValidationError>> Handle(
            ValidateSessionQuery query,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query.AccessToken))
            {
                _logger.LogWarning("ValidateSessionQuery received an empty access token.");
                return SessionValidationError.TokenInvalid;
            }

            var principal = await _authTokenService.ValidateAccessTokenAsync(query.AccessToken);
            if (principal == null)
            {
                _logger.LogInformation("Access token validation failed.");
                return SessionValidationError.TokenInvalid;
            }

            var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(userIdClaim, out var accountId))
            {
                _logger.LogWarning("Access token contains an invalid subject (sub) claim: {SubjectClaim}", userIdClaim);
                return SessionValidationError.TokenInvalid;
            }

            var account = await _accountRepository.GetByIdAsync(accountId);
            if (account == null)
            {
                _logger.LogWarning("Valid token presented for a non-existent account. AccountId: {AccountId}", accountId);
                return SessionValidationError.UserNotFound;
            }

            if (!account.IsActive)
            {
                _logger.LogWarning("Valid token presented for an inactive account. AccountId: {AccountId}", accountId);
                return SessionValidationError.AccountInactive;
            }

            _logger.LogInformation("Session validated successfully for AccountId: {AccountId}", accountId);
            return new ValidateSessionResponse(
                account.Id,
                account.Email,
                account.Roles.Select(r => r.Name).ToList(),
                account.IsActive
            );
        }
    }
}