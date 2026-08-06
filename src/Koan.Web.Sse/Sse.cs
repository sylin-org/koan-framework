using System.Runtime.CompilerServices;
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
    /// <see cref="SseEnvelope"/> values without losing their wire fields.
    /// </summary>
    public static SseResult Stream<T>(
        IAsyncEnumerable<T> source,
        string? eventName = null,
        JsonSerializerSettings? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        serializer ??= DefaultSerializer;

        var isEnvelopeStream = typeof(T) == typeof(SseEnvelope);
        return new SseResult(
            Project(source, eventName, serializer),
            eventName,
            useConfiguredDefault: !isEnvelopeStream);
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
