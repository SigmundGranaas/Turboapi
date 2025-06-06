using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.AuthenticateWithOAuth;
using Turboapi.Infrastructure.Auth;
using Turboapi.Presentation.Cookies;

namespace Turboapi.Presentation.Controllers
{
    public class OAuthController : BaseApiController
    {
        private readonly ICommandHandler<AuthenticateWithOAuthCommand, Result<AuthTokenResponse, OAuthLoginError>> _authHandler;
        private readonly IEnumerable<IOAuthProviderAdapter> _providerAdapters;
        private readonly ICookieManager _cookieManager;
        private readonly JwtConfig _jwtConfig;

        public OAuthController(
            ICommandHandler<AuthenticateWithOAuthCommand, Result<AuthTokenResponse, OAuthLoginError>> authHandler,
            IEnumerable<IOAuthProviderAdapter> providerAdapters,
            ICookieManager cookieManager,
            IOptions<JwtConfig> jwtConfig)
        {
            _authHandler = authHandler;
            _providerAdapters = providerAdapters;
            _cookieManager = cookieManager;
            _jwtConfig = jwtConfig.Value;
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
        public async Task<IActionResult> Callback(string provider, [FromQuery] string code, [FromQuery] string? state)
        {
            var command = new AuthenticateWithOAuthCommand(
                provider,
                code,
                CreateCallbackRedirectUri(provider),
                state);

            var result = await _authHandler.Handle(command, HttpContext.RequestAborted);
            
            result.Switch(
                success => _cookieManager.SetAuthCookies(success.AccessToken, success.RefreshToken, _jwtConfig.TokenExpirationMinutes),
                failure => {}
            );

            return HandleResult(result);
        }
        
        private string CreateCallbackRedirectUri(string provider)
        {
            var scheme = Request.Scheme;
            var host = Request.Host;
            var pathBase = Request.PathBase;
            var action = Url.Action(nameof(Callback), new { provider });

            return $"{scheme}://{host}{pathBase}{action}";
        }
    }
}