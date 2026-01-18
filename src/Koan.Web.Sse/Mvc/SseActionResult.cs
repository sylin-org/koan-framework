using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Sse.Formatting;
using Koan.Web.Sse.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Koan.Web.Sse.Mvc;

public static class SseActionResult
{
    private static readonly JsonSerializerSettings DefaultSerializer = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public static IActionResult StreamJson<T>(IAsyncEnumerable<T> source, string? eventName = null, JsonSerializerSettings? serializer = null)
    {
        return new AsyncEnumerableActionResult(Create(), eventName);

        async IAsyncEnumerable<SseEnvelope> Create([EnumeratorCancellation] CancellationToken ct = default)
        {
            serializer ??= DefaultSerializer;
            await foreach (var item in source.WithCancellation(ct))
            {
                var payload = JsonConvert.SerializeObject(item, Newtonsoft.Json.Formatting.None, serializer);
                yield return new SseEnvelope(eventName, payload);
            }
        }
    }

    public static IActionResult StreamText(IAsyncEnumerable<string> source, string? eventName = null)
    {
        return new AsyncEnumerableActionResult(Enumerate(), eventName);

        async IAsyncEnumerable<SseEnvelope> Enumerate([EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var chunk in source.WithCancellation(ct))
            {
                if (string.IsNullOrEmpty(chunk))
                {
                    continue;
                }

                yield return new SseEnvelope(eventName, chunk);
            }
        }
    }

    public static IActionResult StreamEnvelopes(IAsyncEnumerable<SseEnvelope> source, string? eventName = null)
        => new AsyncEnumerableActionResult(source, eventName);

    private sealed class AsyncEnumerableActionResult : IActionResult
    {
        private readonly IAsyncEnumerable<SseEnvelope> _source;
        private readonly string? _fallbackEvent;

        public AsyncEnumerableActionResult(IAsyncEnumerable<SseEnvelope> source, string? fallbackEvent)
        {
            _source = source;
            _fallbackEvent = string.IsNullOrWhiteSpace(fallbackEvent) ? null : fallbackEvent;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var httpContext = context.HttpContext;
            var services = httpContext.RequestServices;
            var options = services.GetRequiredService<IOptions<KoanSseOptions>>().Value;
            var eventName = _fallbackEvent ?? options.DefaultEvent;

            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Pragma = "no-cache";
            response.Headers.Connection = "keep-alive";
            response.Headers["X-Accel-Buffering"] = "no";

            await foreach (var envelope in _source.WithCancellation(httpContext.RequestAborted))
            {
                var normalized = Normalize(envelope, eventName);
                var payload = SseFormatter.ToWireFormat(normalized);
                await response.WriteAsync(payload, httpContext.RequestAborted);
                await response.Body.FlushAsync(httpContext.RequestAborted);
            }
        }

        private static SseEnvelope Normalize(in SseEnvelope envelope, string eventName)
        {
            if (!envelope.HasEventName)
            {
                return envelope with { EventName = eventName };
            }

            return envelope;
        }
    }
}
