using System.Globalization;
using System.Text.Json;

namespace Koan.Packaging.Models;

internal readonly record struct ReleaseTrain(int Major, int Minor)
{
    public const string FileName = "version.json";
    public const string RequiredFormat =
        "unsigned major.minor with no leading zeros (for example, 1.0)";

    public static ReleaseTrain Parse(string? value)
    {
        var parts = value?.Split('.');
        if (parts is not { Length: 2 } ||
            parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)) ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor))
        {
            throw new InvalidOperationException(
                $"Release train '{value ?? "<missing>"}' must be exactly {RequiredFormat}.");
        }

        var train = new ReleaseTrain(major, minor);
        if (!string.Equals(value, train.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Release train '{value}' must be exactly {RequiredFormat}.");
        }

        return train;
    }

    public static ReleaseTrain ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Version JSON must contain a string 'version' property set to exactly {RequiredFormat}.");
        }

        return Parse(version.GetString());
    }

    public override string ToString() =>
        $"{Major.ToString(CultureInfo.InvariantCulture)}.{Minor.ToString(CultureInfo.InvariantCulture)}";
}
