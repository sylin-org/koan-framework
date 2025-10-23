using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Infrastructure;

/// <summary>
/// Centralized helper for canonical field path handling.
/// Ensures consistent snake_case JSONPath keys across extraction, overrides, and merge.
/// </summary>
public static class FieldPathCanonicalizer
{
    private static readonly Regex UppercaseBoundary = new("(?<=[a-z0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex MustacheToken = new(@"(\{\{\s*[#/^]?\s*)([A-Za-z0-9_.\[\]-]+?)(\s*\}\})", RegexOptions.Compiled);
    private static readonly Regex MustacheTripleToken = new(@"(\{\{\{\s*)([A-Za-z0-9_.\[\]-]+?)(\s*\}\}\})", RegexOptions.Compiled);
    private const string EmptySchema = "{\"type\":\"object\",\"properties\":{}}";

    /// <summary>
    /// Convert an arbitrary field identifier into a canonical JSON path using snake_case segments.
    /// Always emits a path beginning with $. and preserves array segments via [] suffix.
    /// </summary>
    public static string Canonicalize(string? fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "$";
        }

        var trimmed = fieldPath.Trim();
        while (trimmed.StartsWith("$", StringComparison.Ordinal) || trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 0)
        {
            return "$";
        }

        var segments = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder("$");

        foreach (var segment in segments)
        {
            var canonicalSegment = CanonicalizeSegment(segment);
            builder.Append('.').Append(canonicalSegment);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Produce a template key suitable for mustache placeholders (snake_case, no leading $.).
    /// </summary>
    public static string ToTemplateKey(string? fieldPath)
    {
        var canonical = Canonicalize(fieldPath);
        if (canonical == "$")
        {
            return string.Empty;
        }

        return canonical[2..]
            .Replace('.', '_')
            .Replace("[]", "_list", StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes mustache placeholders within a template to use canonical snake_case field names.
    /// </summary>
    public static string CanonicalizeTemplatePlaceholders(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return template ?? string.Empty;
        }

        static string ReplaceToken(Match match)
        {
            var prefix = match.Groups[1].Value;
            var identifier = match.Groups[2].Value;
            var suffix = match.Groups[3].Value;

            if (string.IsNullOrWhiteSpace(identifier) || identifier is "." or ".." || identifier.StartsWith("_", StringComparison.Ordinal))
            {
                return match.Value;
            }

            var canonical = ToTemplateKey(identifier);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                return match.Value;
            }

            return string.Concat(prefix, canonical, suffix);
        }

        var normalized = MustacheTripleToken.Replace(template, ReplaceToken);
        normalized = MustacheToken.Replace(normalized, ReplaceToken);
        return normalized;
    }

    /// <summary>
    /// Normalizes schema property names and required arrays to canonical snake_case identifiers.
    /// </summary>
    public static string CanonicalizeJsonSchema(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return EmptySchema;
        }

        try
        {
            var token = JToken.Parse(schemaJson);
            CanonicalizeSchemaNode(token);
            return token.Type == JTokenType.Null ? EmptySchema : token.ToString(Formatting.None);
        }
        catch (JsonReaderException)
        {
            return schemaJson;
        }
    }

    private static void CanonicalizeSchemaNode(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                var propertiesProperty = obj.Property("properties", StringComparison.OrdinalIgnoreCase);
                if (propertiesProperty?.Value is JObject props)
                {
                    var originals = props.Properties().ToList();
                    props.RemoveAll();

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in originals)
                    {
                        CanonicalizeSchemaNode(property.Value);

                        var canonical = ToTemplateKey(property.Name);
                        if (string.IsNullOrWhiteSpace(canonical))
                        {
                            canonical = property.Name;
                        }

                        if (!seen.Add(canonical))
                        {
                            continue;
                        }

                        props[canonical] = property.Value;
                    }
                }

                var requiredProperty = obj.Property("required", StringComparison.OrdinalIgnoreCase);
                if (requiredProperty?.Value is JArray requiredArray)
                {
                    var canonicalRequired = requiredArray
                        .Values<string?>()
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => ToTemplateKey(value!))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    requiredArray.RemoveAll();
                    foreach (var entry in canonicalRequired)
                    {
                        requiredArray.Add(entry);
                    }
                }

                foreach (var property in obj.Properties())
                {
                    CanonicalizeSchemaNode(property.Value);
                }
                break;

            case JArray array:
                foreach (var item in array)
                {
                    CanonicalizeSchemaNode(item);
                }
                break;
        }
    }

    /// <summary>
    /// Create a human-friendly display name (capitalized words) from a field path.
    /// </summary>
    public static string ToDisplayName(string? fieldPath)
    {
        var canonical = Canonicalize(fieldPath);
        if (canonical == "$")
        {
            return string.Empty;
        }

        var segments = canonical[2..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", segments.Select(seg =>
        {
            var sanitized = seg.Replace("[]", string.Empty, StringComparison.Ordinal);
            if (sanitized.Length == 0)
            {
                return string.Empty;
            }

            var words = sanitized.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        }).Where(value => value.Length > 0));
    }

    /// <summary>
    /// Applies canonicalization to dictionary keys (in place) while preserving values.
    /// </summary>
    public static void CanonicalizeKeys(IDictionary<string, string> dictionary)
    {
        if (dictionary.Count == 0)
        {
            return;
        }

        var snapshot = dictionary.ToArray();
        dictionary.Clear();
        foreach (var (key, value) in snapshot)
        {
            var canonical = Canonicalize(key);
            dictionary[canonical] = value;
        }
    }

    /// <summary>
    /// Applies canonicalization to dictionary keys with arbitrary value types.
    /// </summary>
    public static void CanonicalizeKeys<TValue>(IDictionary<string, TValue> dictionary)
    {
        if (dictionary.Count == 0)
        {
            return;
        }

        var snapshot = dictionary.ToArray();
        dictionary.Clear();
        foreach (var (key, value) in snapshot)
        {
            var canonical = Canonicalize(key);
            dictionary[canonical] = value;
        }
    }

    private static string CanonicalizeSegment(string segment)
    {
        if (segment.EndsWith("[]", StringComparison.Ordinal))
        {
            var withoutArray = segment[..^2];
            return string.Concat(ToSnakeCase(withoutArray), "[]");
        }

        return ToSnakeCase(segment);
    }

    private static string ToSnakeCase(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var snake = UppercaseBoundary.Replace(value, "_$1");
        snake = snake.Replace("-", "_", StringComparison.Ordinal)
                     .Replace(" ", "_", StringComparison.Ordinal);
        return snake.ToLowerInvariant();
    }
}