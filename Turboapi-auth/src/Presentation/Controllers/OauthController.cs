using Microsoft.AspNetCore.Mvc;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.AuthenticateWithOAuth;

namespace Turboapi.Presentation.Controllers
{
    public class OAuthController : BaseApiController
    {
        private readonly ICommandHandler<AuthenticateWithOAuthCommand, Result<AuthTokenResponse, OAuthLoginError>> _authHandler;
        private readonly IEnumerable<IOAuthProviderAdapter> _providerAdapters;

        public OAuthController(
            ICommandHandler<AuthenticateWithOAuthCommand, Result<AuthTokenResponse, OAuthLoginError>> authHandler,
            IEnumerable<IOAuthProviderAdapter> providerAdapters)
        {
            _authHandler = authHandler;
            _providerAdapters = providerAdapters;
        }

        [HttpGet("{provider}/url")]
        public IActionResult GetAuthorizationUrl(string provider, [FromQuery] string? state)
        {
            var adapter = _providerAdapters.FirstOrDefault(p => p.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (adapter == null)
            {
                return NotFound($"Provider '{provider}' not supported.");
            }
            var url = adapter.GetAuthorizationUrl(state);
            return Ok(new { AuthorizationUrl = url });
        }

        [HttpGet("{provider}/callback")]
        [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Callback(string provider, [FromQuery] string code, [FromQuery] string? state)
        {
            var command = new AuthenticateWithOAuthCommand(
                provider,
                code,
                CreateCallbackRedirectUri(provider),
                state);

            var result = await _authHandler.Handle(command, HttpContext.RequestAborted);
            return HandleResult(result);
        }
        
        private string CreateCallbackRedirectUri(string provider)
        {
            return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Url.Action(nameof(Callback), new { provider })}";
        }
    }
}