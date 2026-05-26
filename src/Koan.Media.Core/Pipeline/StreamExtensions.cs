using Koan.Media.Abstractions.Recipes;
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
    public static IMediaPipeline AsMedia(this Stream source, ILogger? logger = null) =>
        MediaPipeline.From(source, logger, disposeSource: true);
}
