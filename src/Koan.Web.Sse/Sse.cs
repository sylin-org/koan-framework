using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace Koan.Web.Sse;

/// <summary>
/// Creates Server-Sent Events results from typed, text, or explicit envelope streams.
/// </summary>
public static class Sse
{
    private static readonly JsonSerializerSettings DefaultSerializer = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    /// <summary>
    /// Streams typed values as compact JSON frames, text values without quoting, and explicit
    /// <see cref="SseEnvelope"/> values without losing their wire fields. The stream writes a comment whenever it has
    /// written nothing for <paramref name="heartbeat"/> (<c>Koan:Web:Sse:Heartbeat</c> when null, none when zero or less)
    /// and ends cleanly after <paramref name="lifetime"/> (until the reader leaves or the source ends when null), for the
    /// reader to reconnect.
    /// </summary>
    public static SseResult Stream<T>(
        IAsyncEnumerable<T> source,
        string? eventName = null,
        JsonSerializerSettings? serializer = null,
        TimeSpan? heartbeat = null,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        serializer ??= DefaultSerializer;

        var isEnvelopeStream = typeof(T) == typeof(SseEnvelope);
        return new SseResult(
            Project(source, eventName, serializer),
            eventName,
            useConfiguredDefault: !isEnvelopeStream,
            heartbeat,
            lifetime);
    }

    /// <summary>
    /// The id a reconnecting reader last received, for the application to resume after: a browser's EventSource sends it by
    /// itself, carrying the <see cref="SseEnvelope.Id"/> of the last frame that had one. Null on a first connection.
    /// </summary>
    public static string? LastEventId(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers["Last-Event-ID"].FirstOrDefault() is { Length: > 0 } id ? id : null;
    }

    private static async IAsyncEnumerable<SseEnvelope> Project<T>(
        IAsyncEnumerable<T> source,
        string? eventName,
        JsonSerializerSettings serializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item is SseEnvelope envelope)
            {
                yield return envelope;
                continue;
            }

            if (item is string text)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    yield return new SseEnvelope(eventName, text);
                }

                continue;
            }

            var payload = JsonConvert.SerializeObject(item, Newtonsoft.Json.Formatting.None, serializer);
            yield return new SseEnvelope(eventName, payload);
        }
    }
}
