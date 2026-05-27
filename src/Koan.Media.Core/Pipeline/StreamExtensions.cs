using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Fonts;
using Microsoft.Extensions.Logging;

namespace Koan.Media.Core.Pipeline;

/// <summary>
/// Single entry point onto the recipe pipeline. Replaces the
/// DX-0047 <c>StreamTransformExtensions</c> surface; callers now
/// chain via the <see cref="IMediaPipeline"/> contract rather than
/// per-method stream extension calls.
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    /// Lift a stream into a media pipeline. The pipeline owns
    /// disposal of the stream after the first terminal call.
    /// </summary>
    /// <param name="source">Source bytes (image format auto-detected).</param>
    /// <param name="logger">Optional logger for destructive-verb diagnostics.</param>
    /// <param name="overlayResolver">Required when the pipeline includes an overlay step backed by media sources.</param>
    /// <param name="fonts">Required when the pipeline includes text overlay layers.</param>
    public static IMediaPipeline AsMedia(
        this Stream source,
        ILogger? logger = null,
        IOverlayResolver? overlayResolver = null,
        KoanFontRegistry? fonts = null) =>
        MediaPipeline.From(source, logger, disposeSource: true, overlayResolver: overlayResolver, fonts: fonts);
}
