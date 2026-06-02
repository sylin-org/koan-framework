using Koan.Media.Core.Pipeline;

namespace Koan.Media.Web.Negotiation;

/// <summary>
/// Resolves the output format slug for a recipe + request pair. Per
/// MEDIA-0009 §d: pure function over the recipe's allowlist, the
/// request's <c>Accept</c> header, and the encoder registry. The
/// resolver is the only piece of MEDIA-0009 that contains logic — every
/// other surface is declarative.
/// </summary>
public static class FormatNegotiator
{
    /// <summary>
    /// Pick the negotiated output format slug for the given inputs.
    /// </summary>
    /// <param name="recipeAllowedFormats">
    /// The recipe's allowlist of producible format slugs in
    /// preferred-default order. When empty the recipe preserves the
    /// source format and negotiation is bypassed.
    /// </param>
    /// <param name="acceptHeader">
    /// Raw <c>Accept</c> header from the request, or null when absent.
    /// Malformed input degrades to the empty list (== "no Accept header
    /// supplied") rather than throwing per MEDIA-0009 §c.
    /// </param>
    /// <param name="sourceFormat">
    /// Canonical slug of the source format — returned verbatim when the
    /// recipe declares no allowlist (preserve-source mode).
    /// </param>
    /// <returns>
    /// The negotiated format slug. Returns <paramref name="sourceFormat"/>
    /// when the allowlist is empty (preserve-source mode). When the
    /// allowlist is set, returns the highest-q match against the encoder
    /// registry's <c>MediaType</c>, falling back to the first allowlist
    /// entry when no Accept entry overlaps.
    /// </returns>
    public static string Negotiate(
        IReadOnlyList<string> recipeAllowedFormats,
        string? acceptHeader,
        string sourceFormat)
    {
        if (recipeAllowedFormats is null || recipeAllowedFormats.Count == 0)
        {
            // Preserve source format — exact pre-MEDIA-0009 behavior.
            return sourceFormat;
        }

        var accepted = AcceptHeaderParser.Parse(acceptHeader);
        if (accepted.Count == 0)
        {
            // Missing or malformed Accept header — recipe's preferred default wins.
            return PreferredProducible(recipeAllowedFormats, sourceFormat);
        }

        // Greedy on q-rank: walk Accept entries in descending q order; for
        // each, scan the recipe's allowlist (preserving its declared
        // preference order) and return the first encoder whose MediaType
        // matches the Accept entry's media-range.
        foreach (var (mediaType, _) in accepted)
        {
            foreach (var slug in recipeAllowedFormats)
            {
                var encoderMediaType = EncoderAccepts.MediaTypeFor(slug);
                if (encoderMediaType is null) continue;
                if (MatchesMediaRange(mediaType, encoderMediaType))
                {
                    return slug;
                }
            }
        }

        // No Accept entry overlapped the allowlist — fall back to the
        // recipe's preferred default per MEDIA-0009 §d.5 (defensive: skip
        // any non-producible allowlist entry so we never promise a format
        // the encoder would 500 on).
        return PreferredProducible(recipeAllowedFormats, sourceFormat);
    }

    /// <summary>
    /// The recipe's preferred default, skipping any allowlisted slug the framework cannot actually
    /// produce. Per MEDIA-0009 §d (defensive fallthrough): a recipe may allowlist a forward-compat
    /// format (e.g. <c>avif</c>) that has no concrete encoder yet — returning it would make the
    /// encoder throw. Falls through to the next producible entry, and to the source format as a last
    /// resort when the entire allowlist is non-producible (a misconfigured recipe — preserve source
    /// rather than 500). The q-rank loop above is already producibility-safe via the null-MediaType
    /// skip; this guards the no-overlap / no-Accept fallbacks, which would otherwise return
    /// allowlist[0] blind.
    /// </summary>
    private static string PreferredProducible(IReadOnlyList<string> recipeAllowedFormats, string sourceFormat)
    {
        foreach (var slug in recipeAllowedFormats)
        {
            if (EncoderAccepts.IsRegistered(slug)) return slug;
        }
        return sourceFormat;
    }

    /// <summary>
    /// True when the Accept media-range admits the encoder's MIME type.
    /// Handles <c>*/*</c>, <c>type/*</c>, and exact <c>type/subtype</c>
    /// matching per RFC 9110 §12.5.1.
    /// </summary>
    private static bool MatchesMediaRange(string acceptMediaRange, string encoderMediaType)
    {
        // Wildcard everything.
        if (acceptMediaRange == "*/*") return true;

        var aSlash = acceptMediaRange.IndexOf('/');
        var eSlash = encoderMediaType.IndexOf('/');
        if (aSlash <= 0 || eSlash <= 0) return false;

        var aType = acceptMediaRange.AsSpan(0, aSlash);
        var aSub = acceptMediaRange.AsSpan(aSlash + 1);
        var eType = encoderMediaType.AsSpan(0, eSlash);
        var eSub = encoderMediaType.AsSpan(eSlash + 1);

        // type/* wildcard
        if (aSub.Length == 1 && aSub[0] == '*')
        {
            return aType.Equals(eType, StringComparison.OrdinalIgnoreCase);
        }

        return aType.Equals(eType, StringComparison.OrdinalIgnoreCase)
            && aSub.Equals(eSub, StringComparison.OrdinalIgnoreCase);
    }
}
