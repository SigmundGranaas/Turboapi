using Microsoft.AspNetCore.Mvc;
using Turboapi.Application.Contracts.V1.Auth;
using Turboapi.Application.Interfaces;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;
using Turboapi.Application.UseCases.Commands.LoginUserWithPassword;
using Turboapi.Application.UseCases.Commands.RegisterUserWithPassword;

namespace Turboapi.Presentation.Controllers
{
    public class AuthController : BaseApiController
    {
        private readonly ICommandHandler<RegisterUserWithPasswordCommand, Result<AuthTokenResponse, RegistrationError>> _registerHandler;
        private readonly ICommandHandler<LoginUserWithPasswordCommand, Result<AuthTokenResponse, LoginError>> _loginHandler;

        public AuthController(
            ICommandHandler<RegisterUserWithPasswordCommand, Result<AuthTokenResponse, RegistrationError>> registerHandler,
            ICommandHandler<LoginUserWithPasswordCommand, Result<AuthTokenResponse, LoginError>> loginHandler)
        {
            _registerHandler = registerHandler;
            _loginHandler = loginHandler;
        }

        [HttpPost("register")]
        [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Register([FromBody] RegisterUserWithPasswordRequest request)
        {
            if (request.Password != request.ConfirmPassword)
            {
                return BadRequest("Passwords do not match.");
            }
            var command = new RegisterUserWithPasswordCommand(request.Email, request.Password);
            var result = await _registerHandler.Handle(command, HttpContext.RequestAborted);
            return HandleResult(result);
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Login([FromBody] LoginUserWithPasswordRequest request)
        {
            var command = new LoginUserWithPasswordCommand(request.Email, request.Password);
            var result = await _loginHandler.Handle(command, HttpContext.RequestAborted);
            return HandleResult(result);
        }
    }
}