using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Declared encoder capability table. Per MEDIA-0005 §3, encoder
/// admission is data — not a switch case in the picker. Extended by
/// MEDIA-0009 §a: each encoder now declares both what it admits
/// (<see cref="EncoderDescriptor.InputAccepts"/>) and what it produces
/// (<see cref="EncoderDescriptor.FormatSlug"/>,
/// <see cref="EncoderDescriptor.MediaType"/>,
/// <see cref="EncoderDescriptor.PreservesAnimation"/>), so the format
/// negotiator can intersect the recipe's allowlist, the encoder
/// registry, and the request's <c>Accept</c> header against a single
/// declarative source of truth.
///
/// Adding a new encoder is a one-line registration here plus the
/// encoder binding in <see cref="EncoderSelector"/>.
/// </summary>
public static class EncoderAccepts
{
    /// <summary>
    /// Declarative descriptor for a registered encoder. Per MEDIA-0009 §a.
    /// </summary>
    /// <param name="FormatSlug">Canonical lowercase output slug (e.g. <c>"webp"</c>).</param>
    /// <param name="InputAccepts">Kind admission for the source — what this encoder can read.</param>
    /// <param name="MediaType">Output MIME type (e.g. <c>"image/webp"</c>) used for <c>Accept</c> matching.</param>
    /// <param name="PreservesAnimation">True when this encoder retains animation frames; false when it flattens.</param>
    public sealed record EncoderDescriptor(
        string FormatSlug,
        KindSet InputAccepts,
        string MediaType,
        bool PreservesAnimation);

    private static readonly IReadOnlyDictionary<string, EncoderDescriptor> _descriptors =
        new Dictionary<string, EncoderDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            // Still-only raster encoders.
            ["jpeg"] = new EncoderDescriptor("jpeg", KindSet.Of(MediaKind.Raster), "image/jpeg", PreservesAnimation: false),
            ["png"] = new EncoderDescriptor("png", KindSet.Of(MediaKind.Raster), "image/png", PreservesAnimation: false),
            ["bmp"] = new EncoderDescriptor("bmp", KindSet.Of(MediaKind.Raster), "image/bmp", PreservesAnimation: false),
            ["tiff"] = new EncoderDescriptor("tiff", KindSet.Of(MediaKind.Raster), "image/tiff", PreservesAnimation: false),

            // Animated-capable raster encoders.
            ["webp"] = new EncoderDescriptor("webp", KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster), "image/webp", PreservesAnimation: true),
            ["gif"] = new EncoderDescriptor("gif", KindSet.Of(MediaKind.Raster, MediaKind.AnimatedRaster), "image/gif", PreservesAnimation: true),

            // AVIF: declared for forward-compat, but ImageSharp's AvifEncoder is not in the current
            // package surface, so EncoderSelector cannot produce it. It is therefore filtered out of
            // the live registry (see _live) and never negotiated; recipes that allowlist "avif" fall
            // through to their next-preferred producible format. Wiring the encoder into
            // EncoderSelector flips this descriptor live automatically (DATA-0098).
            ["avif"] = new EncoderDescriptor("avif", KindSet.Of(MediaKind.Raster), "image/avif", PreservesAnimation: false),
        };

    /// <summary>
    /// The LIVE encoder registry: only the declared formats the producer (<see cref="EncoderSelector"/>)
    /// can actually emit. DATA-0098 single-source-of-truth — the capability table is DERIVED from what
    /// can be produced, never maintained as an independent list. A format declared above but with no
    /// concrete encoder (e.g. <c>avif</c> until ImageSharp's AvifEncoder is wired into EncoderSelector)
    /// is excluded here, so the negotiator never advertises or selects it and the next-preferred
    /// producible format wins. Re-wiring is automatic: add the encoder to EncoderSelector and the
    /// already-declared descriptor goes live.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, EncoderDescriptor> _live =
        _descriptors
            .Where(kv => EncoderSelector.SupportedFormats.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Live encoder registry keyed by canonical format slug — only formats the framework can actually
    /// produce. This is what the negotiator and planner intersect against.
    /// </summary>
    public static IReadOnlyDictionary<string, EncoderDescriptor> All => _live;

    /// <summary>
    /// Set of kinds the named encoder accepts. Returns
    /// <see cref="KindSet.None"/> for unknown/non-producible slugs (planner will
    /// produce an EncoderRefused-equivalent error against an empty set).
    /// </summary>
    public static KindSet AcceptsFor(string formatSlug)
    {
        if (string.IsNullOrWhiteSpace(formatSlug)) return KindSet.None;
        return _live.TryGetValue(formatSlug, out var d) ? d.InputAccepts : KindSet.None;
    }

    /// <summary>
    /// MIME media type for the named encoder, or null when the slug is
    /// unregistered or not producible.
    /// </summary>
    public static string? MediaTypeFor(string formatSlug)
    {
        if (string.IsNullOrWhiteSpace(formatSlug)) return null;
        return _live.TryGetValue(formatSlug, out var d) ? d.MediaType : null;
    }

    /// <summary>True when the named encoder admits <see cref="MediaKind.AnimatedRaster"/>.</summary>
    public static bool IsAnimatedCapable(string formatSlug) =>
        AcceptsFor(formatSlug).Contains(MediaKind.AnimatedRaster);

    /// <summary>True when the named encoder preserves animation frames on output.</summary>
    public static bool PreservesAnimation(string formatSlug)
    {
        if (string.IsNullOrWhiteSpace(formatSlug)) return false;
        return _live.TryGetValue(formatSlug, out var d) && d.PreservesAnimation;
    }

    /// <summary>True when the encoder slug is registered AND producible.</summary>
    public static bool IsRegistered(string formatSlug) =>
        !string.IsNullOrWhiteSpace(formatSlug) && _live.ContainsKey(formatSlug);
}
