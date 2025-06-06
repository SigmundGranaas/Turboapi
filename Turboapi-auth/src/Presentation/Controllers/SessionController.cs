using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turboapi.Application.UseCases.Queries.ValidateSession;
using Turboapi.Domain.Interfaces;

namespace Turboapi.Presentation.Controllers
{
    [Authorize(AuthenticationSchemes = $"{CookieAuthenticationDefaults.AuthenticationScheme},{JwtBearerDefaults.AuthenticationScheme}")]
    public class SessionController : BaseApiController
    {
        private readonly IAccountRepository _accountRepository;

        public SessionController(IAccountRepository accountRepository)
        {
            _accountRepository = accountRepository;
        }

        [HttpGet("me")]
        [ProducesResponseType(typeof(ValidateSessionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetCurrentUser()
        {
            // The [Authorize] attribute ensures that by the time this code is reached,
            // HttpContext.User has been populated by a successful authentication (either cookie or bearer).
            var accountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) 
                                 ?? User.FindFirstValue("sub");

            if (!Guid.TryParse(accountIdClaim, out var accountId))
            {
                // This case should be rare, as a valid token is required to get this far.
                return Unauthorized("Invalid token format: 'sub' claim is not a valid GUID.");
            }

            var account = await _accountRepository.GetByIdAsync(accountId);
            if (account == null)
            {
                // A valid token was presented for a user that no longer exists.
                return Unauthorized();
            }

            if (!account.IsActive)
            {
                // The user is valid but not allowed to access the system.
                return Forbid();
            }

            var response = new ValidateSessionResponse(
                accountId,
                User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
                account.IsActive
            );

            return Ok(response);
        }
    }
}