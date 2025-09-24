using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;

namespace Koan.Web.Backup.Middleware;

/// <summary>
/// Middleware for handling errors in backup API endpoints
/// </summary>
public class BackupApiErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BackupApiErrorHandlingMiddleware> _logger;

    public BackupApiErrorHandlingMiddleware(RequestDelegate next, ILogger<BackupApiErrorHandlingMiddleware> logger)
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
            _logger.LogError(ex, "Unhandled exception occurred in backup API");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse();

        switch (exception)
        {
            case ArgumentException argEx:
                response.Status = (int)HttpStatusCode.BadRequest;
                response.Title = "Invalid Argument";
                response.Detail = argEx.Message;
                break;

            case InvalidOperationException invOpEx:
                response.Status = (int)HttpStatusCode.BadRequest;
                response.Title = "Invalid Operation";
                response.Detail = invOpEx.Message;
                break;

            case UnauthorizedAccessException unAuthEx:
                response.Status = (int)HttpStatusCode.Unauthorized;
                response.Title = "Unauthorized";
                response.Detail = unAuthEx.Message;
                break;

            case FileNotFoundException fileNotFoundEx:
                response.Status = (int)HttpStatusCode.NotFound;
                response.Title = "Backup Not Found";
                response.Detail = fileNotFoundEx.Message;
                break;

            case TimeoutException timeoutEx:
                response.Status = (int)HttpStatusCode.RequestTimeout;
                response.Title = "Operation Timeout";
                response.Detail = timeoutEx.Message;
                break;

            case OperationCanceledException cancelEx:
                response.Status = (int)HttpStatusCode.BadRequest;
                response.Title = "Operation Cancelled";
                response.Detail = cancelEx.Message;
                break;

            default:
                response.Status = (int)HttpStatusCode.InternalServerError;
                response.Title = "Internal Server Error";
                response.Detail = "An unexpected error occurred while processing the request.";
                break;
        }

        context.Response.StatusCode = response.Status;

        var jsonResponse = JsonConvert.SerializeObject(response, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// Standard error response format
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// HTTP status code
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// Error title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed error description
    /// </summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// Request trace ID for debugging
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Additional error metadata
    /// </summary>
    public Dictionary<string, object>? Extensions { get; set; }
}

/// <summary>
/// Extension methods for registering error handling middleware
/// </summary>
public static class ErrorHandlingMiddlewareExtensions
{
    /// <summary>
    /// Add backup API error handling middleware to the pipeline
    /// </summary>
    /// <param name="builder">Application builder</param>
    /// <returns>Application builder for chaining</returns>
    public static IApplicationBuilder UseBackupApiErrorHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<BackupApiErrorHandlingMiddleware>();
    }
}