namespace Koan.Data.Abstractions.Naming;

public static class NamingUtils
{
    /// <summary>
    /// Deterministic short hash (lowercase hex) of an arbitrary string, for collision-resistant identifier
    /// disambiguation (namespace folding, length-overflow clamping). Stable across processes/runs.
    /// </summary>
    public static string ShortHash(string value, int hexChars = 8)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value ?? ""));
        var byteCount = System.Math.Clamp((hexChars + 1) / 2, 1, bytes.Length);
        return System.Convert.ToHexString(bytes, 0, byteCount).ToLowerInvariant()[..hexChars];
    }

    /// <summary>UTF-8 byte length of <paramref name="value"/> — identifier limits are byte-based.</summary>
    public static int ByteLength(string value) => System.Text.Encoding.UTF8.GetByteCount(value ?? "");

    /// <summary>Truncate <paramref name="value"/> so its UTF-8 byte length does not exceed <paramref name="maxBytes"/>.</summary>
    public static string TrimToBytes(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || ByteLength(value) <= maxBytes) return value ?? "";
        var take = System.Math.Min(value.Length, maxBytes);
        while (take > 0 && ByteLength(value[..take]) > maxBytes) take--;
        return value[..take];
    }

    public static string ApplyCase(string value, NameCasing casing)
    {
        return casing switch
        {
            NameCasing.AsIs => value,
            NameCasing.Lower => value.ToLowerInvariant(),
            NameCasing.Upper => value.ToUpperInvariant(),
            NameCasing.Snake => ToDelimited(value, '_'),
            NameCasing.Kebab => ToDelimited(value, '-'),
            NameCasing.Pascal => ToPascalOrCamel(value, pascal: true),
            NameCasing.Camel => ToPascalOrCamel(value, pascal: false),
            _ => value
        };
    }

    private static string ToPascalOrCamel(string input, bool pascal)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var parts = SplitWords(input);
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (i == 0 && !pascal) parts[i] = p[..1].ToLowerInvariant() + p[1..];
            else parts[i] = p[..1].ToUpperInvariant() + p[1..].ToLowerInvariant();
        }
        return string.Concat(parts);
    }

    private static string ToDelimited(string input, char sep)
    {
        var parts = SplitWords(input);
        for (int i = 0; i < parts.Count; i++) parts[i] = parts[i].ToLowerInvariant();
        return string.Join(sep, parts);
    }

    private static List<string> SplitWords(string input)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in input)
        {
            if (ch == '_' || ch == '-' || ch == ' ')
            {
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
            }
            else if (char.IsUpper(ch) && sb.Length > 0)
            {
                list.Add(sb.ToString()); sb.Clear(); sb.Append(ch);
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }
}
