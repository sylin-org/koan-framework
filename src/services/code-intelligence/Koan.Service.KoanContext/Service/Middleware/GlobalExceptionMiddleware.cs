using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Middleware;

/// <summary>
/// Global exception handler that prevents information disclosure in production
/// </summary>
/// <remarks>
/// Security implementation:
/// - Production: Returns generic error messages, no stack traces
/// - Development: Returns detailed error information for debugging
/// - All exceptions logged server-side with full details
/// - Structured error responses for API clients
/// </remarks>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Generate correlation ID for tracking
        var correlationId = Guid.NewGuid().ToString();

        // Log full exception details server-side
        _logger.LogError(
            exception,
            "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}, Method: {Method}",
            correlationId,
            context.Request.Path,
            context.Request.Method);

        // Determine status code based on exception type
        var (statusCode, errorType) = GetStatusCodeAndType(exception);

        // Build error response
        var errorResponse = BuildErrorResponse(exception, correlationId, errorType);

        // Set response properties
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        // Serialize and write response
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        };

        var json = JsonSerializer.Serialize(errorResponse, jsonOptions);
        await context.Response.WriteAsync(json);
    }

    private (int statusCode, string errorType) GetStatusCodeAndType(Exception exception)
    {
        return exception switch
        {
            // More specific exceptions first (inheritance hierarchy)
            ArgumentNullException => ((int)HttpStatusCode.BadRequest, "validation_error"),
            ArgumentException => ((int)HttpStatusCode.BadRequest, "validation_error"),
            InvalidOperationException => ((int)HttpStatusCode.BadRequest, "invalid_operation"),
            UnauthorizedAccessException => ((int)HttpStatusCode.Forbidden, "access_denied"),
            FileNotFoundException => ((int)HttpStatusCode.NotFound, "not_found"),
            DirectoryNotFoundException => ((int)HttpStatusCode.NotFound, "not_found"),
            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "not_found"),
            NotSupportedException => ((int)HttpStatusCode.BadRequest, "not_supported"),
            TimeoutException => ((int)HttpStatusCode.RequestTimeout, "timeout"),
            TaskCanceledException => ((int)HttpStatusCode.RequestTimeout, "cancelled"),
            OperationCanceledException => ((int)HttpStatusCode.RequestTimeout, "cancelled"),
            _ => ((int)HttpStatusCode.InternalServerError, "internal_error")
        };
    }

    private object BuildErrorResponse(Exception exception, string correlationId, string errorType)
    {
        if (_environment.IsDevelopment())
        {
            // Development: Include detailed error information
            return new
            {
                error = new
                {
                    type = errorType,
                    message = exception.Message,
                    correlationId,
                    timestamp = DateTime.UtcNow,
                    details = new
                    {
                        exceptionType = exception.GetType().Name,
                        stackTrace = exception.StackTrace,
                        innerException = exception.InnerException?.Message,
                        source = exception.Source
                    }
                }
            };
        }
        else
        {
            // Production: Return sanitized error message
            var userMessage = GetUserFriendlyMessage(exception, errorType);

            return new
            {
                error = new
                {
                    type = errorType,
                    message = userMessage,
                    correlationId,
                    timestamp = DateTime.UtcNow,
                    // Help users report issues
                    support = new
                    {
                        message = "If this problem persists, please contact support with the correlation ID",
                        correlationId
                    }
                }
            };
        }
    }

    private string GetUserFriendlyMessage(Exception exception, string errorType)
    {
        // Return safe, user-friendly messages without exposing internal details
        return errorType switch
        {
            "validation_error" => "The request contains invalid data. Please check your input and try again.",
            "invalid_operation" => "The requested operation could not be completed. Please verify your request.",
            "access_denied" => "You do not have permission to perform this action.",
            "not_found" => "The requested resource was not found.",
            "not_supported" => "This operation is not supported.",
            "timeout" => "The request timed out. Please try again later.",
            "cancelled" => "The operation was cancelled.",
            "internal_error" => "An unexpected error occurred. Please try again later.",
            _ => "An error occurred while processing your request."
        };
    }
}

/// <summary>
/// Extension methods for registering global exception handling
/// </summary>
public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
