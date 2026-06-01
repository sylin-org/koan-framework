using System.Globalization;
using System.Text;
using System.Xml;
using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Formats;

/// <summary>
/// SVG header sniff + viewBox extraction. Per MEDIA-0006 §Decision.1.
///
/// Detection cannot rely on <c>Content-Type</c> (uploads, fetches, and cached
/// blobs all lie or omit it) and cannot rely on filename extension
/// (<c>UrlContentCache</c> strips it). It must inspect bytes.
/// </summary>
public static class SvgFormat
{
    /// <summary>Canonical format slug carried on <see cref="MediaInfo"/>.</summary>
    public const string Slug = "svg";

    /// <summary>MIME type for SVG payloads.</summary>
    public const string MediaType = "image/svg+xml";

    /// <summary>Maximum byte length scanned for the SVG header sniff.</summary>
    public const int HeaderSniffBytes = 1024;

    /// <summary>
    /// Inspect the leading bytes of the source. Accept when the prefix is
    /// either bare <c>&lt;svg</c> (after optional whitespace / BOM) or an XML
    /// prolog (<c>&lt;?xml</c>) whose first <c>&lt;svg</c> tag appears within
    /// the inspected window. Reject when the first non-whitespace byte is
    /// not <c>&lt;</c>.
    /// </summary>
    public static bool MatchHeader(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty) return false;

        // Strip UTF-8 BOM if present so the prefix checks below match raw '<'.
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            head = head[3..];
        }

        // Skip leading whitespace.
        var i = 0;
        while (i < head.Length && IsXmlWhitespace(head[i])) i++;
        if (i >= head.Length) return false;

        // First non-whitespace byte must open a tag.
        if (head[i] != (byte)'<') return false;

        // Decode the inspected window as UTF-8 (with replacement); we only need
        // the prefix to look for the literal token "<svg" (case-insensitive).
        // SVG documents typically open with "<?xml" or "<svg"; DOCTYPE-bearing
        // SVGs are rejected later by the validator's hard-prefix scan.
        var text = Encoding.UTF8.GetString(head);
        var lower = text.ToLowerInvariant();

        if (lower.AsSpan().TrimStart().StartsWith("<svg", StringComparison.Ordinal))
        {
            return true;
        }

        if (lower.AsSpan().TrimStart().StartsWith("<?xml", StringComparison.Ordinal))
        {
            // First <svg must appear within the inspected window.
            return lower.Contains("<svg", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Walk the root <c>&lt;svg&gt;</c> element via <see cref="XmlReader"/>
    /// and resolve display dimensions from either <c>viewBox</c> or the
    /// <c>width</c>/<c>height</c> attribute pair. Returns <c>(0, 0)</c> when
    /// neither is parseable — callers (validator / probe) treat that as
    /// terminal.
    /// </summary>
    public static (int Width, int Height) ReadViewBoxOrIntrinsicSize(XmlReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        // Advance to the root <svg> element. We assume the caller has just
        // opened the reader; skip XML declaration / whitespace / comments
        // until we land on the first Element node.
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (!string.Equals(reader.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
            {
                // Root element is not <svg>; defer to the validator's
                // element-allowlist gate to produce the canonical failure.
                return (0, 0);
            }
            break;
        }

        if (reader.NodeType != XmlNodeType.Element || !string.Equals(reader.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0);
        }

        var viewBox = reader.GetAttribute("viewBox");
        if (TryParseViewBox(viewBox, out var vbW, out var vbH))
        {
            return (vbW, vbH);
        }

        var widthAttr = reader.GetAttribute("width");
        var heightAttr = reader.GetAttribute("height");
        if (TryParseLength(widthAttr, out var w) && TryParseLength(heightAttr, out var h))
        {
            return (w, h);
        }

        return (0, 0);
    }

    private static bool IsXmlWhitespace(byte b) =>
        b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';

    private static bool TryParseViewBox(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // viewBox = "min-x min-y width height" with whitespace OR commas as separators.
        var span = value.AsSpan();
        Span<double> parts = stackalloc double[4];
        var idx = 0;
        var start = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            var atEnd = i == span.Length;
            var isSep = !atEnd && (char.IsWhiteSpace(span[i]) || span[i] == ',');
            if (atEnd || isSep)
            {
                if (i > start)
                {
                    if (idx >= 4) return false;
                    var token = span[start..i];
                    if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        return false;
                    }
                    parts[idx++] = v;
                }
                start = i + 1;
            }
        }
        if (idx != 4) return false;

        // Width / height live at indices 2 / 3 per SVG spec.
        var w = parts[2];
        var h = parts[3];
        if (double.IsNaN(w) || double.IsNaN(h) || double.IsInfinity(w) || double.IsInfinity(h)) return false;
        if (w <= 0 || h <= 0) return false;
        width = (int)Math.Round(w);
        height = (int)Math.Round(h);
        return width > 0 && height > 0;
    }

    private static bool TryParseLength(string? value, out int pixels)
    {
        pixels = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Strip a trailing unit token (px, pt, pc, mm, cm, in, em, ex, %).
        // The numeric prefix is what we keep. Per CSS spec px == 1 user unit,
        // which is what the SVG viewport scales by — close enough for an
        // intrinsic-size fallback.
        var span = value.AsSpan().Trim();
        var cut = span.Length;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (char.IsLetter(c) || c == '%')
            {
                cut = i;
                break;
            }
        }
        var numericPart = span[..cut];
        if (!double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }
        if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) return false;
        pixels = (int)Math.Round(v);
        return pixels > 0;
    }
}
