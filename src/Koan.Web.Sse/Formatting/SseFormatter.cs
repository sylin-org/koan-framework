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

        RefuseLineBreak(envelope.Comment, "comment");
        RefuseLineBreak(envelope.EventName, "event name");
        RefuseLineBreak(envelope.Id, "id");

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

    // A line break in a single-line field ends it and begins whatever follows as another field, so text that reached one
    // from a person could forge a frame. Refused; data is the one field that may span lines.
    private static void RefuseLineBreak(string? value, string field)
    {
        if (value is not null && value.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException(
                $"An SSE {field} cannot hold a line break, which would end the field and begin another: '{value.ReplaceLineEndings(" ")}'.",
                nameof(value));
        }
    }

    // A reader's parser ends a line at CR, LF or CRLF, so each of them begins a new data line here.
    private static void AppendDataLines(StringBuilder builder, string data)
    {
        var rest = data.AsSpan();
        while (rest.IndexOfAny('\r', '\n') is var at and >= 0)
        {
            builder.Append("data: ").Append(rest[..at]).Append('\n');
            var isCrLf = rest[at] == '\r' && at + 1 < rest.Length && rest[at + 1] == '\n';
            rest = rest[(at + (isCrLf ? 2 : 1))..];
        }

        builder.Append("data: ").Append(rest).Append('\n');
    }
}
