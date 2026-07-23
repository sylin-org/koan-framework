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
    private readonly bool _useConfiguredDefault;

    internal SseResult(
        IAsyncEnumerable<SseEnvelope> source,
        string? fallbackEvent,
        bool useConfiguredDefault)
    {
        _source = source;
        _fallbackEvent = string.IsNullOrWhiteSpace(fallbackEvent) ? null : fallbackEvent.Trim();
        _useConfiguredDefault = useConfiguredDefault;
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
        var defaultEvent = _fallbackEvent ?? (_useConfiguredDefault ? options.DefaultEvent : null);
        var response = httpContext.Response;

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        ComposeHeaders(response);

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

    private static void ComposeHeaders(HttpResponse response)
    {
        var cacheControl = response.Headers.CacheControl.ToString();
        if (string.IsNullOrWhiteSpace(cacheControl))
        {
            response.Headers.CacheControl = "no-cache";
        }
        else if (!HasCacheDirective(cacheControl, "no-cache") &&
                 !HasCacheDirective(cacheControl, "no-store"))
        {
            response.Headers.CacheControl = $"{cacheControl}, no-cache";
        }

        response.Headers.TryAdd("Pragma", "no-cache");
        response.Headers.TryAdd("Connection", "keep-alive");
        response.Headers.TryAdd("X-Accel-Buffering", "no");
    }

    private static bool HasCacheDirective(string value, string directive)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => candidate.Equals(directive, StringComparison.OrdinalIgnoreCase) ||
                              candidate.StartsWith($"{directive}=", StringComparison.OrdinalIgnoreCase));
}
