using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Koan.Data.Connector.Json.Runtime;

/// <summary>Compiles one safe individual-file path template for one physical Entity storage name.</summary>
internal sealed class JsonIndividualFileLocator
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string _root;
    private readonly string _prefix;
    private readonly string _suffix;
    private readonly string _searchRoot;
    private readonly string _searchPattern;
    private readonly SearchOption _searchOption;

    internal JsonIndividualFileLocator(string directoryPath, string template, string storageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageName);

        _root = Path.GetFullPath(directoryPath);
        var normalized = NormalizeAndValidate(template);
        UsesStorageToken = normalized.Contains(Infrastructure.Constants.Storage.StorageToken, StringComparison.Ordinal);

        var rendered = normalized.Replace(
            Infrastructure.Constants.Storage.StorageToken,
            EncodeToken(storageName),
            StringComparison.Ordinal);
        var idIndex = rendered.IndexOf(Infrastructure.Constants.Storage.IdToken, StringComparison.Ordinal);
        _prefix = rendered[..idIndex];
        _suffix = rendered[(idIndex + Infrastructure.Constants.Storage.IdToken.Length)..];

        var slashBeforeId = rendered.LastIndexOf('/', idIndex);
        var searchRootRelative = slashBeforeId < 0 ? "" : rendered[..slashBeforeId];
        _searchRoot = ContainedPath(searchRootRelative);

        var fileSegment = rendered[(rendered.LastIndexOf('/') + 1)..];
        _searchPattern = fileSegment.Replace(
            Infrastructure.Constants.Storage.IdToken,
            "*",
            StringComparison.Ordinal);
        _searchOption = rendered.IndexOf('/', idIndex + Infrastructure.Constants.Storage.IdToken.Length) >= 0
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;
    }

    internal bool UsesStorageToken { get; }

    internal string PathFor<TKey>(TKey id) where TKey : notnull
    {
        var encoded = EncodeToken(KeyText(id));
        if (encoded.Length == 0)
            throw new InvalidOperationException("JSON individual-file identity cannot render as an empty path token.");
        return ContainedPath(_prefix + encoded + _suffix);
    }

    internal IEnumerable<string> EnumeratePaths()
    {
        if (!Directory.Exists(_searchRoot)) yield break;

        foreach (var path in Directory.EnumerateFiles(_searchRoot, _searchPattern, _searchOption))
        {
            var full = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(_root, full).Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
                relative = relative.Replace(Path.AltDirectorySeparatorChar, '/');
            if (Matches(relative)) yield return full;
        }
    }

    private bool Matches(string relative)
    {
        if (!relative.StartsWith(_prefix, PathComparison) || !relative.EndsWith(_suffix, PathComparison))
            return false;

        var tokenLength = relative.Length - _prefix.Length - _suffix.Length;
        if (tokenLength <= 0) return false;
        var token = relative.AsSpan(_prefix.Length, tokenLength);
        return token.IndexOf('/') < 0 && token.IndexOf('\\') < 0;
    }

    private string ContainedPath(string relative)
    {
        ValidateRenderedRelative(relative);
        var platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, platformRelative));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        if (!string.Equals(full, _root, PathComparison) && !full.StartsWith(rootWithSeparator, PathComparison))
        {
            throw new InvalidOperationException(
                $"JSON individual-file path '{relative}' resolves outside source directory '{_root}'.");
        }
        return full;
    }

    private static void ValidateRenderedRelative(string relative)
    {
        if (relative.Length == 0) return;

        foreach (var segment in relative.Split('/'))
        {
            if (segment.Length == 0 || segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(static character => character < ' ' || "<>:\"|?*".Contains(character)) ||
                IsWindowsDeviceName(segment))
            {
                throw new InvalidOperationException(
                    $"JSON individual-file path '{relative}' contains a platform-unsafe path segment '{segment}'.");
            }
        }
    }

    private static string NormalizeAndValidate(string template)
    {
        if (Path.IsPathRooted(template))
            throw InvalidTemplate(template, "it must be relative to DirectoryPath");

        var normalized = template.Replace('\\', '/').Trim();
        if (normalized.Length == 0 || normalized[0] == '/')
            throw InvalidTemplate(template, "it must be a non-empty relative path");
        if (!normalized.EndsWith(Infrastructure.Constants.Storage.Extension, StringComparison.OrdinalIgnoreCase))
            throw InvalidTemplate(template, $"it must end with '{Infrastructure.Constants.Storage.Extension}'");

        var idCount = Count(normalized, Infrastructure.Constants.Storage.IdToken);
        if (idCount != 1)
            throw InvalidTemplate(template, $"it must contain exactly one '{Infrastructure.Constants.Storage.IdToken}' token");
        if (Count(normalized, Infrastructure.Constants.Storage.StorageToken) > 1)
            throw InvalidTemplate(template, $"it may contain at most one '{Infrastructure.Constants.Storage.StorageToken}' token");

        var withoutKnownTokens = normalized
            .Replace(Infrastructure.Constants.Storage.IdToken, "", StringComparison.Ordinal)
            .Replace(Infrastructure.Constants.Storage.StorageToken, "", StringComparison.Ordinal);
        if (withoutKnownTokens.Contains('{') || withoutKnownTokens.Contains('}'))
            throw InvalidTemplate(template, "it contains an unknown or malformed token");

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
                throw InvalidTemplate(template, "it contains an empty or traversing path segment");
        }

        return normalized;
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }
        return count;
    }

    private static string KeyText<TKey>(TKey id) where TKey : notnull
    {
        if (id is string text) return text;
        if (id is Guid guid) return guid.ToString("D");
        if (id is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture) ?? "";

        var converter = TypeDescriptor.GetConverter(typeof(TKey));
        return converter.CanConvertTo(typeof(string))
            ? converter.ConvertToInvariantString(id) ?? ""
            : id.ToString() ?? "";
    }

    private static string EncodeToken(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var encoded = new StringBuilder(bytes.Length);
        foreach (var valueByte in bytes)
        {
            if (valueByte is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'_')
            {
                encoded.Append((char)valueByte);
            }
            else
            {
                encoded.Append('%').Append(valueByte.ToString("X2", CultureInfo.InvariantCulture));
            }
        }
        var result = encoded.ToString();
        if (!IsWindowsDeviceName(result)) return result;

        // Device names are reserved even with an extension on Windows. Encoding the first byte keeps the mapping
        // stable across platforms while preserving a one-to-one identity token.
        return $"%{(byte)result[0]:X2}{result[1..]}";
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        var stem = segment.Split('.', 2)[0].TrimEnd(' ', '.');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 && stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && stem[3] is >= '1' and <= '9') ||
               (stem.Length == 4 && stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) && stem[3] is >= '1' and <= '9');
    }

    private static InvalidOperationException InvalidTemplate(string template, string reason) => new(
        $"JSON IndividualFilePath '{template}' is invalid: {reason}. Use a relative JSON path with one " +
        $"'{Infrastructure.Constants.Storage.IdToken}' token and, when storage isolation is required, one " +
        $"'{Infrastructure.Constants.Storage.StorageToken}' token.");
}
