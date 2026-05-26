namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Canonical pipeline execution stages. Steps are always executed in
/// stage order regardless of how they were declared in the recipe or
/// the URL query string. Per MEDIA-0004 §1, this fixed ordering is
/// what makes <c>?w=600&amp;format=png</c> and <c>?format=png&amp;w=600</c>
/// produce identical bytes — encoding is always terminal, decoding is
/// always first.
/// </summary>
public enum PipelineStage
{
    /// <summary>Implicit; loads source bytes into an Image with all frames.</summary>
    Decode = 0,

    /// <summary>EXIF-based auto-orient. Default-on, opt-out via <c>?orient=keep</c>.</summary>
    Orient = 10,

    /// <summary>Animated → still via <c>ExtractFrame</c>. No-op on static sources.</summary>
    Frame = 20,

    /// <summary>
    /// Timeline operations — video-only (trim, speed, concat). Image
    /// pipeline ignores. Reserved slot so <c>Koan.Media.Video</c>
    /// ships without enum reshuffling.
    /// </summary>
    Timeline = 25,

    /// <summary>Explicit rotation / flip after EXIF normalisation.</summary>
    Rotate = 30,

    /// <summary>Single shape slot: crop + fit + position + bg.</summary>
    Shape = 40,

    /// <summary>Single size slot: resize / scale.</summary>
    Size = 50,

    /// <summary>Composition layers (overlays).</summary>
    Overlay = 60,

    /// <summary>EXIF / ICC / XMP metadata stripping.</summary>
    Metadata = 70,

    /// <summary>
    /// Audio operations — video-only (mute, extract, normalize).
    /// Image pipeline ignores. Reserved slot so <c>Koan.Media.Video</c>
    /// ships without enum reshuffling.
    /// </summary>
    Audio = 75,

    /// <summary>Always terminal. Default: preserve source format.</summary>
    Encode = 80,
}
