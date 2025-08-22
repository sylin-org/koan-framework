namespace Sora.Data.Abstractions.Naming;

/// <summary>
/// Casing transforms for derived storage names.
/// </summary>
public enum NameCasing
{
    AsIs = 0,
    Lower,
    Upper,
    Pascal,
    Camel,
    Snake,
    Kebab
}

public static class NamingUtils
{
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
