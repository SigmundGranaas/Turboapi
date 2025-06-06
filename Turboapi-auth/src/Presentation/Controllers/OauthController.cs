using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.AuthenticateWithOAuth;
using Turboapi.Infrastructure.Auth;
using Turboapi.Infrastructure.Auth.OAuthProviders;
using Turboapi.Presentation.Cookies;

namespace Turboapi.Presentation.Controllers
{
    [Route("api/auth/[controller]")]
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
            
            // On successful web auth, redirect to the main app page
            if (result.IsSuccess)
            {
                // This assumes a front-end running on the same domain or a configured one.
                // You might need a more dynamic redirect URI from configuration.
                return Redirect("http://localhost:8080/login/success"); // Or your main app URL
            }

            return HandleResult(result);
        }
        
        private string CreateCallbackRedirectUri(string provider)
        {
            // This method must construct the exact URI that was sent to the provider.
            // It should match the one configured in `appsettings.Development.json` for Google.
            // Using a hardcoded value from config is safer than dynamically building it.
            var googleSettings = HttpContext.RequestServices.GetRequiredService<IOptions<GoogleAuthSettings>>().Value;
            return googleSettings.RedirectUri;
        }
    }
}