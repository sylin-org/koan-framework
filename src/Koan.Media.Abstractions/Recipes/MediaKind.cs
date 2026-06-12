namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// The four media kinds the planner discriminates. Per MEDIA-0005 §1.
///
/// A decoder declares the kind it produces. The planner reads it and
/// threads it through each step. No step asks "what format was this?" —
/// that question is answered once at decode time and never re-litigated.
/// </summary>
public enum MediaKind
{
    /// <summary>Single frame, width × height, pixel buffer.</summary>
    Raster = 0,

    /// <summary>N frames with per-frame timing, dimensions, loop count.</summary>
    AnimatedRaster = 1,

    /// <summary>Device-independent extents, no pixel grid yet.</summary>
    Vector = 2,

    /// <summary>Time-indexed video, duration, framerate, audio track (opaque).</summary>
    Timeline = 3,
}
