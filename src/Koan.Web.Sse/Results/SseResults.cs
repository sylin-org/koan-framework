using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Sse.Formatting;
using Koan.Web.Sse.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Koan.Web.Sse.Results;

/// <summary>
/// Helpers for producing SSE responses from Minimal API endpoints.
/// </summary>
public static class SseResults
{
    private static readonly JsonSerializerSettings DefaultSerializer = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public static IResult StreamJson<T>(IAsyncEnumerable<T> source, string? eventName = null, JsonSerializerSettings? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        serializer ??= DefaultSerializer;

        return new AsyncEnumerableSseResult(CreateAsyncEnumerator(), eventName);

        async IAsyncEnumerable<SseEnvelope> CreateAsyncEnumerator([EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var item in source.WithCancellation(ct))
            {
                var payload = JsonConvert.SerializeObject(item, Newtonsoft.Json.Formatting.None, serializer);
                yield return new SseEnvelope(eventName, payload);
            }
        }
    }

    public static IResult StreamText(IAsyncEnumerable<string> source, string? eventName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AsyncEnumerableSseResult(Enumerate(), eventName);

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

    public static IResult StreamEnvelopes(IAsyncEnumerable<SseEnvelope> source, string? eventName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AsyncEnumerableSseResult(source, eventName);
    }

    private sealed class AsyncEnumerableSseResult : IResult
    {
        private readonly IAsyncEnumerable<SseEnvelope> _source;
        private readonly string? _fallbackEvent;

        public AsyncEnumerableSseResult(IAsyncEnumerable<SseEnvelope> source, string? fallbackEvent)
        {
            _source = source;
            _fallbackEvent = string.IsNullOrWhiteSpace(fallbackEvent) ? null : fallbackEvent;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            if (httpContext is null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

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

            await foreach (var rawEnvelope in _source.WithCancellation(httpContext.RequestAborted))
            {
                var envelope = Normalize(rawEnvelope, eventName);
                var payload = SseFormatter.ToWireFormat(envelope);
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
