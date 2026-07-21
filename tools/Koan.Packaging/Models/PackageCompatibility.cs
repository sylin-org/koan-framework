namespace Koan.Packaging.Models;

internal sealed record PackageCompatibility(string Floor, string Ceiling)
{
    public string Range => $"[{Floor}, {Ceiling})";

    public static PackageCompatibility FromVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0)
        {
            throw new InvalidOperationException(
                $"Package version '{value}' cannot define a Koan compatibility band. Expected a stable major.minor.patch version.");
        }

        var ceiling = version.Major == 0
            ? new Version(0, version.Minor + 1, 0)
            : new Version(version.Major + 1, 0, 0);
        return new PackageCompatibility(value, ceiling.ToString(3));
    }

    public static bool TryParseRange(string value, out PackageCompatibility? compatibility)
    {
        compatibility = null;
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!normalized.StartsWith("[", StringComparison.Ordinal) ||
            !normalized.EndsWith(")", StringComparison.Ordinal)) return false;
        var parts = normalized[1..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        try
        {
            var expected = FromVersion(parts[0]);
            if (!string.Equals(expected.Ceiling, parts[1], StringComparison.OrdinalIgnoreCase)) return false;
            compatibility = expected;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
