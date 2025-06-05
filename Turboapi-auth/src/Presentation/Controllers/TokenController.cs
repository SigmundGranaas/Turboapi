using Microsoft.AspNetCore.Mvc;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Contracts.V1.Tokens;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.RefreshToken;

namespace Turboapi.Presentation.Controllers
{
    public class TokenController : BaseApiController
    {
        private readonly ICommandHandler<RefreshTokenCommand, Result<AuthTokenResponse, RefreshTokenError>> _refreshHandler;

        public TokenController(ICommandHandler<RefreshTokenCommand, Result<AuthTokenResponse, RefreshTokenError>> refreshHandler)
        {
            _refreshHandler = refreshHandler;
        }

        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
        {
            var command = new RefreshTokenCommand(request.RefreshToken);
            var result = await _refreshHandler.Handle(command, HttpContext.RequestAborted);
            return HandleResult(result);
        }
    }
}