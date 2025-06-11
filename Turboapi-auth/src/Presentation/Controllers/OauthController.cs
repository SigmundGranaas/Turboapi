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
        private readonly IConfiguration _configuration;

        public OAuthController(
            ICommandHandler<AuthenticateWithOAuthCommand, Result<AuthTokenResponse, OAuthLoginError>> authHandler,
            IEnumerable<IOAuthProviderAdapter> providerAdapters,
            ICookieManager cookieManager,
            IOptions<JwtConfig> jwtConfig,
            IConfiguration configuration)
        {
            _authHandler = authHandler;
            _providerAdapters = providerAdapters;
            _cookieManager = cookieManager;
            _jwtConfig = jwtConfig.Value;
            _configuration = configuration;
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
            
            return result.Match<IActionResult>(
                success =>
                {
                    // For web flow, set the session cookies
                    _cookieManager.SetAuthCookies(success.AccessToken, success.RefreshToken, _jwtConfig.TokenExpirationMinutes);

                    // Check if the client is an API client (like Flutter mobile) expecting JSON,
                    // or a browser that needs a redirect.
                    var isApiRequest = Request.Headers.Accept.ToString().Contains("application/json");

                    if (isApiRequest)
                    {
                        // For mobile/API clients, return the tokens directly in the body.
                        return Ok(success);
                    }
                    else
                    {
                        // For web clients (browsers), redirect to the frontend success page.
                        var frontendUrl = _configuration.GetValue<string>("FrontendUrl") ?? "http://localhost:8080";
                        return Redirect($"{frontendUrl}/login/success");
                    }
                },
                failure => HandleResult(result) // HandleResult maps the error to a status code.
            );
        }
        
        private string CreateCallbackRedirectUri(string provider)
        {
            var googleSettings = HttpContext.RequestServices.GetRequiredService<IOptions<GoogleAuthSettings>>().Value;
            return googleSettings.RedirectUri;
        }
    }
}