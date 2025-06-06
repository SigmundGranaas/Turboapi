using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.LoginUserWithPassword;
using Turboapi.Application.UseCases.Commands.RegisterUserWithPassword;
using Turboapi.Infrastructure.Auth;
using Turboapi.Presentation.Cookies;

namespace Turboapi.Presentation.Controllers
{
    public class AuthController : BaseApiController
    {
        private readonly ICommandHandler<RegisterUserWithPasswordCommand, Result<AuthTokenResponse, RegistrationError>> _registerHandler;
        private readonly ICommandHandler<LoginUserWithPasswordCommand, Result<AuthTokenResponse, LoginError>> _loginHandler;
        private readonly ICookieManager _cookieManager;
        private readonly JwtConfig _jwtConfig;

        public AuthController(
            ICommandHandler<RegisterUserWithPasswordCommand, Result<AuthTokenResponse, RegistrationError>> registerHandler,
            ICommandHandler<LoginUserWithPasswordCommand, Result<AuthTokenResponse, LoginError>> loginHandler,
            ICookieManager cookieManager,
            IOptions<JwtConfig> jwtConfig)
        {
            _registerHandler = registerHandler;
            _loginHandler = loginHandler;
            _cookieManager = cookieManager;
            _jwtConfig = jwtConfig.Value;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserWithPasswordRequest request)
        {
            if (request.Password != request.ConfirmPassword)
            {
                return BadRequest("Passwords do not match.");
            }
            var command = new RegisterUserWithPasswordCommand(request.Email, request.Password);
            var result = await _registerHandler.Handle(command, HttpContext.RequestAborted);
            
            result.Switch(
                success => _cookieManager.SetAuthCookies(success.AccessToken, success.RefreshToken, _jwtConfig.TokenExpirationMinutes),
                failure => {}
            );

            return HandleResult(result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginUserWithPasswordRequest request)
        {
            var command = new LoginUserWithPasswordCommand(request.Email, request.Password);
            var result = await _loginHandler.Handle(command, HttpContext.RequestAborted);
            
            result.Switch(
                success => _cookieManager.SetAuthCookies(success.AccessToken, success.RefreshToken, _jwtConfig.TokenExpirationMinutes),
                failure => {}
            );

            return HandleResult(result);
        }
    }
}