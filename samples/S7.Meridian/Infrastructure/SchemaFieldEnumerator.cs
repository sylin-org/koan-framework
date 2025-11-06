using System;
using System.Collections.Generic;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Infrastructure;

/// <summary>
/// Shared helper for enumerating canonical JSON schema field paths.
/// </summary>
public static class SchemaFieldEnumerator
{
    public static IEnumerable<(string FieldPath, JSchema Schema)> EnumerateLeaves(JSchema? root)
    {
        if (root is null)
        {
            yield break;
        }

        foreach (var entry in EnumerateInternal(root, "$"))
        {
            yield return entry;
        }
    }

    public static ISet<string> BuildCanonicalFieldSet(JSchema? root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (fieldPath, _) in EnumerateLeaves(root))
        {
            set.Add(fieldPath);
        }

        return set;
    }

    private static IEnumerable<(string FieldPath, JSchema Schema)> EnumerateInternal(JSchema schema, string prefix)
    {
        if (schema.Type == JSchemaType.Object && schema.Properties.Count > 0)
        {
            foreach (var property in schema.Properties)
            {
                var nextPrefix = prefix == "$"
                    ? $"$.{property.Key}"
                    : $"{prefix}.{property.Key}";

                foreach (var nested in EnumerateInternal(property.Value, nextPrefix))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (schema.Type == JSchemaType.Array && schema.Items.Count > 0)
        {
            var nextPrefix = prefix.EndsWith("[]", StringComparison.Ordinal)
                ? prefix
                : $"{prefix}[]";

            foreach (var nested in EnumerateInternal(schema.Items[0], nextPrefix))
            {
                yield return nested;
            }

            yield break;
        }

        yield return (FieldPathCanonicalizer.Canonicalize(prefix), schema);
    }
}
