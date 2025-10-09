using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace S16.PantryPal.Infrastructure;

public sealed class ErrorEnvelopeFilter : IExceptionFilter
{
    private readonly ILogger<ErrorEnvelopeFilter> _logger;
    public ErrorEnvelopeFilter(ILogger<ErrorEnvelopeFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        var ex = context.Exception;
        _logger.LogError(ex, "Unhandled exception");
        context.Result = new ObjectResult(new { error = ex.Message, type = ex.GetType().Name })
        {
            StatusCode = 500
        };
        context.ExceptionHandled = true;
    }
}