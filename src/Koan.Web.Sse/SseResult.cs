using Koan.Web.Sse.Formatting;
using Koan.Web.Sse.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Sse;

/// <summary>
/// Executes one SSE stream through either ASP.NET MVC or framework transport infrastructure.
/// </summary>
public sealed class SseResult : IActionResult, IResult
{
    private readonly IAsyncEnumerable<SseEnvelope> _source;
    private readonly string? _fallbackEvent;

    internal SseResult(IAsyncEnumerable<SseEnvelope> source, string? fallbackEvent)
    {
        _source = source;
        _fallbackEvent = string.IsNullOrWhiteSpace(fallbackEvent) ? null : fallbackEvent.Trim();
    }

    /// <inheritdoc />
    public Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteAsync(context.HttpContext);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var options = httpContext.RequestServices.GetRequiredService<IOptions<KoanSseOptions>>().Value;
        var defaultEvent = _fallbackEvent ?? options.DefaultEvent;
        var response = httpContext.Response;

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Pragma = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var envelope in _source
            .WithCancellation(httpContext.RequestAborted)
            .ConfigureAwait(false))
        {
            var normalized = envelope.HasEventName
                ? envelope
                : envelope with { EventName = defaultEvent };
            var payload = SseFormatter.ToWireFormat(normalized);

            await response.WriteAsync(payload, httpContext.RequestAborted).ConfigureAwait(false);
            await response.Body.FlushAsync(httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
