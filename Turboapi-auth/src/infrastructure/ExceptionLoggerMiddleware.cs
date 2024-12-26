namespace Turboapi.infrastructure;

public class ExceptionLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionLoggingMiddleware> _logger;

    public ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception: {ExceptionType} - {Message}\nStack Trace: {StackTrace}\nSource: {Source}",
                ex.GetType().Name,
                ex.Message,
                ex.StackTrace,
                ex.Source);
            throw;
        }
    }
}