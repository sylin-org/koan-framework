namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Named quality presets that appear as canonical names in
/// <c>/media/recipes</c> JSON. Recipes use these instead of magic
/// numbers so the introspection surface stays human-readable and
/// presets can be tuned in one place. Per MEDIA-0004 §4.
/// </summary>
public static class Quality
{
    /// <summary>~60 — small thumbnails, contact sheets, low-stakes previews.</summary>
    public const int Thumbnail = 60;

    /// <summary>~80 — default for hero images and content surfaces.</summary>
    public const int Web = 80;

    /// <summary>~95 — archival / print-ready.</summary>
    public const int Print = 95;

    /// <summary>
    /// Sentinel for "encoder picks lossless mode if available."
    /// WebP encoder honours via FileFormat = Lossless; PNG ignores
    /// (PNG is always lossless); JPEG falls back to <see cref="Print"/>.
    /// </summary>
    public const int Lossless = -1;

    /// <summary>
    /// Map an int to its canonical preset name when one matches,
    /// otherwise return the numeric form. Used by <c>/media/recipes</c>
    /// JSON serialisation.
    /// </summary>
    public static string ToCanonical(int quality) => quality switch
    {
        Thumbnail => "thumbnail",
        Web => "web",
        Print => "print",
        Lossless => "lossless",
        _ => quality.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Resolve a canonical preset name back to its int value. Returns
    /// false for unknown names. Numeric input ("85") parses via
    /// <see cref="int.TryParse(string?, out int)"/>.
    /// </summary>
    public static bool TryParse(string? value, out int quality)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            quality = Web;
            return false;
        }
        switch (value.Trim().ToLowerInvariant())
        {
            case "thumbnail": quality = Thumbnail; return true;
            case "web": quality = Web; return true;
            case "print": quality = Print; return true;
            case "lossless": quality = Lossless; return true;
            default:
                if (int.TryParse(
                        value,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out quality))
                {
                    return true;
                }
                // Unknown input — fall back to the Web default so callers
                // that ignore the bool still get a sane value.
                quality = Web;
                return false;
        }
    }
}
