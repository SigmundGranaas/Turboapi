using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turboapi.Application.UseCases.Queries.ValidateSession;

namespace Turboapi.Presentation.Controllers
{
    [Authorize]
    public class SessionController : BaseApiController
    {
        // No handler is needed anymore. The controller interacts directly with HttpContext.User.
        public SessionController()
        {
        }

        [HttpGet("me")] // Changed to a GET request as it's idempotent and retrieves state.
        [ProducesResponseType(typeof(ValidateSessionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult GetCurrentUser()
        {
            var accountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) 
                                 ?? User.FindFirstValue("sub");

            if (!Guid.TryParse(accountIdClaim, out var accountId))
            {
                // This should not happen if the token was issued by us, but it's good practice.
                return Unauthorized("Invalid token format: 'sub' claim is not a valid GUID.");
            }

            var response = new ValidateSessionResponse(
                accountId,
                User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
                true // If we are here, the account must be active (based on previous logic)
            );

            return Ok(response);
        }
    }
}