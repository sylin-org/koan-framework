using System.Diagnostics;
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
    private readonly TimeSpan? _heartbeat;
    private readonly TimeSpan? _lifetime;

    private static readonly SseEnvelope Beat = new(null, "", Comment: "heartbeat");

    internal SseResult(
        IAsyncEnumerable<SseEnvelope> source,
        string? fallbackEvent,
        bool useConfiguredDefault,
        TimeSpan? heartbeat,
        TimeSpan? lifetime)
    {
        _source = source;
        _fallbackEvent = string.IsNullOrWhiteSpace(fallbackEvent) ? null : fallbackEvent.Trim();
        _useConfiguredDefault = useConfiguredDefault;
        _heartbeat = heartbeat;
        _lifetime = lifetime;
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
        var heartbeat = _heartbeat ?? options.Heartbeat;
        var response = httpContext.Response;

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        ComposeHeaders(response);

        var aborted = httpContext.RequestAborted;
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        var source = _source.GetAsyncEnumerator(ending.Token);
        var opened = Stopwatch.GetTimestamp();
        var wrote = opened;
        var next = source.MoveNextAsync().AsTask();
        try
        {
            while (true)
            {
                var wait = Timeout.InfiniteTimeSpan;
                if (_lifetime is { } lifetime)
                {
                    wait = lifetime - Stopwatch.GetElapsedTime(opened);
                    if (wait <= TimeSpan.Zero) return;
                }

                if (heartbeat > TimeSpan.Zero)
                {
                    var beat = heartbeat - Stopwatch.GetElapsedTime(wrote);
                    if (beat <= TimeSpan.Zero)
                    {
                        await Write(response, Beat, aborted).ConfigureAwait(false);
                        wrote = Stopwatch.GetTimestamp();
                        continue;
                    }

                    if (wait == Timeout.InfiniteTimeSpan || beat < wait) wait = beat;
                }

                // A wait that passes leaves the source's read pending, to be waited on again: the source is never
                // cancelled for being quiet. A failure of the source surfaces below, once its read has completed.
                await ((Task)next.WaitAsync(wait, aborted)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                aborted.ThrowIfCancellationRequested();
                if (!next.IsCompleted) continue;
                if (!await next.ConfigureAwait(false)) return;

                var envelope = source.Current;
                var normalized = envelope.HasEventName
                    ? envelope
                    : envelope with { EventName = defaultEvent };
                await Write(response, normalized, aborted).ConfigureAwait(false);
                wrote = Stopwatch.GetTimestamp();
                next = source.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            // An enumerator cannot be disposed with a read in flight, whichever way the stream ended.
            await ending.CancelAsync().ConfigureAwait(false);
            await ((Task)next).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task Write(HttpResponse response, SseEnvelope envelope, CancellationToken ct)
    {
        await response.WriteAsync(SseFormatter.ToWireFormat(envelope), ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
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
