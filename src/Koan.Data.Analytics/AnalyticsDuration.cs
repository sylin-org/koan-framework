namespace Koan.Data.Analytics;

/// <summary>
/// Duration spellings the freshness door accepts: <c>90s</c>, <c>15m</c>, <c>2h</c>, <c>1d</c>, or a
/// plain number of seconds. A malformed or negative value refuses loudly — silently coercing garbage
/// to "always fresh" or "never fresh" would both lie.
/// </summary>
public static class AnalyticsDuration
{
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var span = text.Trim().ToLowerInvariant();

        if (span.EndsWith("d", StringComparison.Ordinal) && long.TryParse(span[..^1], out var days))
        { if (days < 0) return false; value = TimeSpan.FromDays(days); return true; }
        if (span.EndsWith("h", StringComparison.Ordinal) && long.TryParse(span[..^1], out var hours))
        { if (hours < 0) return false; value = TimeSpan.FromHours(hours); return true; }
        if (span.EndsWith("m", StringComparison.Ordinal) && long.TryParse(span[..^1], out var minutes))
        { if (minutes < 0) return false; value = TimeSpan.FromMinutes(minutes); return true; }
        if (span.EndsWith("s", StringComparison.Ordinal) && long.TryParse(span[..^1], out var seconds))
        { if (seconds < 0) return false; value = TimeSpan.FromSeconds(seconds); return true; }
        if (long.TryParse(span, out var plain))
        { if (plain < 0) return false; value = TimeSpan.FromSeconds(plain); return true; }
        return false;
    }
}
