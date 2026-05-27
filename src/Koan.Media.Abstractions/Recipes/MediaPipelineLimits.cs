namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Pre-decode safety caps applied by the pipeline before committing to
/// the expensive full-decode pass. Per MEDIA-0004 §13. Zero on any
/// field disables that specific check; <see cref="Unlimited"/> disables
/// every check.
///
/// <para>The pipeline checks these via <see cref="SixLabors.ImageSharp.Image.IdentifyAsync"/>
/// (header-only read) before <see cref="SixLabors.ImageSharp.Image.LoadAsync"/>
/// so a malicious 50000×50000 PNG can be rejected without ever
/// allocating the decoded buffer.</para>
/// </summary>
public sealed record MediaPipelineLimits
{
    /// <summary>Reject sources whose pixel count exceeds this value. Zero = unlimited.</summary>
    public int MaxSourceMegapixels { get; init; }

    /// <summary>Reject animated sources with more than this many frames. Zero = unlimited.</summary>
    public int MaxFrameCount { get; init; }

    public static MediaPipelineLimits Unlimited { get; } = new();

    /// <summary>Convenience for the documented MEDIA-0004 §13 defaults.</summary>
    public static MediaPipelineLimits Defaults { get; } = new()
    {
        MaxSourceMegapixels = 100,
        MaxFrameCount = 600,
    };
}

/// <summary>
/// Thrown when a source exceeds one of the configured
/// <see cref="MediaPipelineLimits"/>. The HTTP layer maps this to
/// 400 with the <c>X-Koan-Media-LimitExceeded</c> diagnostic header.
/// </summary>
public sealed class MediaSourceLimitException : Exception
{
    /// <summary>Short identifier for the limit that fired (e.g. <c>maxSourceMegapixels</c>).</summary>
    public string LimitName { get; }

    /// <summary>The actual value that exceeded the limit.</summary>
    public long Value { get; }

    /// <summary>The configured cap.</summary>
    public long Cap { get; }

    public MediaSourceLimitException(string limitName, long value, long cap)
        : base($"Source exceeded limit '{limitName}': value={value}, cap={cap}.")
    {
        LimitName = limitName;
        Value = value;
        Cap = cap;
    }
}
