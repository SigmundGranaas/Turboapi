using Microsoft.AspNetCore.Mvc;
using Turboapi.Application.Contracts.V1.Common;
using Turboapi.Application.Results;
using Turboapi.Application.Results.Errors;

namespace Turboapi.Presentation.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public abstract class BaseApiController : ControllerBase
    {
        protected IActionResult HandleResult<TError>(Result<TError> result)
            where TError : Enum
        {
            if (result.IsSuccess)
            {
                return NoContent();
            }

            return HandleError(result.Error);
        }
        
        protected IActionResult HandleResult<TSuccess, TError>(Result<TSuccess, TError> result)
            where TError : Enum
        {
            
            if (result.IsSuccess)
            {
                // For void-like success results (e.g., Result<SuccessUnit, TError>)
                if (typeof(TSuccess) == typeof(OkResult))
                {
                    return Ok();
                }
                return Ok(result.Value);
            }

            return HandleError(result.Error);
        }
        
        private IActionResult HandleError<TError>(TError error) where TError : Enum
        {
            var errorCode = error.GetType().Name;
            var errorMessage = error.ToString();

            var statusCode = error switch
            {
                RegistrationError e when e == RegistrationError.EmailAlreadyExists => StatusCodes.Status409Conflict,
                LoginError e when e == LoginError.InvalidCredentials => StatusCodes.Status401Unauthorized,
                LoginError e when e == LoginError.AccountNotFound => StatusCodes.Status404NotFound,
                OAuthLoginError e when e == OAuthLoginError.EmailNotVerified => StatusCodes.Status403Forbidden,
                SessionValidationError e when e == SessionValidationError.AccountInactive => StatusCodes.Status403Forbidden,
                SessionValidationError e when e == SessionValidationError.UserNotFound => StatusCodes.Status404NotFound,
                SessionValidationError e when e == SessionValidationError.TokenInvalid => StatusCodes.Status401Unauthorized,
                RefreshTokenError e when e == RefreshTokenError.InvalidToken => StatusCodes.Status401Unauthorized,
                RefreshTokenError e when e == RefreshTokenError.Revoked => StatusCodes.Status401Unauthorized,
                RefreshTokenError e when e == RefreshTokenError.Expired => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status400BadRequest
            };
            
            var errorResponse = new ErrorResponse(errorCode, errorMessage);
            return new ObjectResult(errorResponse) { StatusCode = statusCode };
        }
    }
}