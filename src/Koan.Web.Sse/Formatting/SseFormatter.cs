using System;
using System.Globalization;
using System.Text;

namespace Koan.Web.Sse.Formatting;

/// <summary>
/// Formats <see cref="SseEnvelope"/> instances into wire-friendly strings.
/// </summary>
public static class SseFormatter
{
    public static string ToWireFormat(in SseEnvelope envelope)
    {
        if (envelope.Data is null)
        {
            throw new ArgumentNullException(nameof(envelope), "SSE data payload cannot be null.");
        }

        var builder = new StringBuilder();

        if (envelope.HasComment)
        {
            builder.Append(':').Append(' ').Append(envelope.Comment).Append('\n');
        }

        if (!envelope.IsControlFrame && envelope.HasEventName)
        {
            builder.Append("event: ").Append(envelope.EventName).Append('\n');
        }

        if (!string.IsNullOrEmpty(envelope.Id))
        {
            builder.Append("id: ").Append(envelope.Id).Append('\n');
        }

        if (envelope.Retry is { } retry)
        {
            builder.Append("retry: ")
                .Append(Math.Max(0, (int)retry.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        if (!envelope.IsControlFrame)
        {
            AppendDataLines(builder, envelope.Data);
        }

        builder.Append('\n');
        return builder.ToString();
    }

    private static void AppendDataLines(StringBuilder builder, string data)
    {
        var span = data.AsSpan();
        var start = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '\n')
            {
                builder.Append("data: ").Append(span.Slice(start, i - start)).Append('\n');
                start = i + 1;
            }
        }

        if (start <= span.Length)
        {
            builder.Append("data: ").Append(span[start..]).Append('\n');
        }
    }
}
