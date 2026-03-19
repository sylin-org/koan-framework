using System.Globalization;
using System.Text.RegularExpressions;

namespace Koan.Storage.Replication;

/// <summary>
/// Configuration for the local cache tier in replicated storage.
/// When absent from a profile, the cache is unbounded (no eviction, full mirror).
/// </summary>
public sealed partial class LocalCacheOptions
{
    /// <summary>
    /// Maximum cache size as a human-readable string (e.g., "500MB", "1GB", "2.5TB").
    /// Null or empty means unlimited.
    /// </summary>
    public string? MaxSize { get; set; }

    /// <summary>
    /// Start evicting when cache usage exceeds this percentage of quota (0–100).
    /// </summary>
    public int HighWatermark { get; set; } = 90;

    /// <summary>
    /// Stop evicting when cache usage drops to this percentage of quota (0–100).
    /// </summary>
    public int LowWatermark { get; set; } = 70;

    /// <summary>
    /// Eviction policy. "lru" (least recently used) or "pinned" (never evict).
    /// </summary>
    public string Policy { get; set; } = "lru";

    /// <summary>
    /// Parses <see cref="MaxSize"/> into bytes.
    /// Supports: KB, MB, GB, TB (case-insensitive). Plain number treated as bytes.
    /// Returns null when MaxSize is null, empty, or cannot be parsed.
    /// </summary>
    public long? ParseMaxSizeBytes()
    {
        if (string.IsNullOrWhiteSpace(MaxSize))
            return null;

        var match = SizePattern().Match(MaxSize.Trim());
        if (!match.Success)
            return null;

        if (!decimal.TryParse(match.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return null;

        var unit = match.Groups["unit"].Value.ToUpperInvariant();

        var multiplier = unit switch
        {
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            "B" => 1L,
            "" => 1L,
            _ => -1L
        };

        if (multiplier < 0)
            return null;

        return (long)(value * multiplier);
    }

    [GeneratedRegex(@"^(?<value>\d+(?:\.\d+)?)\s*(?<unit>[A-Za-z]*)$", RegexOptions.Compiled)]
    private static partial Regex SizePattern();
}
