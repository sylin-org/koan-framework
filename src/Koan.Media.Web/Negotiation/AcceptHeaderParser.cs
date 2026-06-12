using System.Globalization;

namespace Koan.Media.Web.Negotiation;

/// <summary>
/// Parses an HTTP <c>Accept</c> header into q-ranked media-range
/// entries, per RFC 9110 §12.5.1. Per MEDIA-0009 §c: malformed input
/// degrades silently to an empty list — the network is never trusted
/// to send well-formed tokens.
///
/// <para>The parser is allocation-light: one
/// <see cref="System.Collections.Generic.List{T}"/> for the result
/// plus the temporary <see cref="string"/>s produced by
/// <see cref="string.Split(char, System.StringSplitOptions)"/>.</para>
/// </summary>
public static class AcceptHeaderParser
{
    /// <summary>
    /// Parse <paramref name="acceptHeader"/> into media-type + q-value
    /// pairs, sorted by q descending then by source-order ascending.
    /// Empty / null input returns an empty list.
    /// </summary>
    /// <param name="acceptHeader">The raw <c>Accept</c> header value (may be null).</param>
    /// <returns>
    /// Q-ranked list of <c>(MediaType, Q)</c> pairs. Each
    /// <see cref="string"/> MediaType is canonicalised to lowercase.
    /// Entries with <c>q=0</c> are dropped per RFC 9110 §12.5.1.
    /// </returns>
    public static IReadOnlyList<(string MediaType, double Q)> Parse(string? acceptHeader)
    {
        if (string.IsNullOrWhiteSpace(acceptHeader))
        {
            return Array.Empty<(string, double)>();
        }

        var entries = new List<(string MediaType, double Q, int Order)>();
        var tokens = acceptHeader.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var order = 0;
        foreach (var raw in tokens)
        {
            order++;
            var token = raw.Trim();
            if (token.Length == 0) continue;

            var parts = token.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var mediaType = parts[0].Trim().ToLowerInvariant();
            if (!IsLikelyMediaType(mediaType))
            {
                // Malformed token — skip silently per MEDIA-0009 §c.
                continue;
            }

            var q = 1.0;
            for (var i = 1; i < parts.Length; i++)
            {
                var param = parts[i].Trim();
                if (param.Length < 2) continue;
                if (!param.StartsWith("q=", StringComparison.OrdinalIgnoreCase)) continue;

                var qRaw = param.AsSpan(2).Trim();
                if (!double.TryParse(qRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    // Garbage q-value — skip the entry per MEDIA-0009 §c.
                    q = double.NaN;
                    break;
                }
                q = Math.Clamp(parsed, 0.0, 1.0);
            }

            if (double.IsNaN(q)) continue;
            if (q <= 0.0) continue; // q=0 explicitly drops the entry.

            entries.Add((mediaType, q, order));
        }

        if (entries.Count == 0)
        {
            return Array.Empty<(string, double)>();
        }

        entries.Sort(static (a, b) =>
        {
            var c = b.Q.CompareTo(a.Q);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        });

        var result = new List<(string MediaType, double Q)>(entries.Count);
        foreach (var (mt, q, _) in entries)
        {
            result.Add((mt, q));
        }
        return result;
    }

    private static bool IsLikelyMediaType(string token)
    {
        // Accept entries are "type/subtype" — anything missing the slash
        // is malformed and dropped per MEDIA-0009 §c.
        var slash = token.IndexOf('/');
        if (slash <= 0 || slash >= token.Length - 1) return false;
        return true;
    }
}
