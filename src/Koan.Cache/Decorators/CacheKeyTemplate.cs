using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Koan.Cache.Decorators;

internal sealed class CacheKeyTemplate
{
    private static readonly ConcurrentDictionary<string, CacheKeyTemplate> Cache = new(StringComparer.Ordinal);

    private readonly Segment[] _segments;

    private CacheKeyTemplate(string template, Segment[] segments)
    {
        Template = template;
        _segments = segments;
    }

    public string Template { get; }

    public static CacheKeyTemplate For(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new ArgumentException("Template must be provided.", nameof(template));
        }

        return Cache.GetOrAdd(template, static t => new CacheKeyTemplate(t, ParseSegments(t)));
    }

    public string? TryFormat(object? entity, IReadOnlyDictionary<string, object?> ambient, out bool missingToken)
    {
        if (_segments.Length == 0)
        {
            missingToken = false;
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (!segment.IsPlaceholder)
            {
                builder.Append(segment.Content);
                continue;
            }

            if (!TryResolveValue(segment.Content, entity, ambient, out var value))
            {
                missingToken = true;
                return null;
            }

            var rendered = ConvertToString(value);
            if (rendered is null)
            {
                missingToken = true;
                return null;
            }

            builder.Append(rendered);
        }

        missingToken = false;
        return builder.ToString();
    }

    private static Segment[] ParseSegments(string template)
    {
        var segments = new List<Segment>();
        var span = template.AsSpan();
        var builder = new StringBuilder();
        var position = 0;
        var inToken = false;

        while (position < span.Length)
        {
            var ch = span[position];
            if (ch == '{')
            {
                if (position + 1 < span.Length && span[position + 1] == '{')
                {
                    builder.Append('{');
                    position += 2;
                    continue;
                }

                if (inToken)
                {
                    throw new FormatException($"Nested '{{' detected in cache key template '{template}'.");
                }

                if (builder.Length > 0)
                {
                    segments.Add(new Segment(builder.ToString(), false));
                    builder.Clear();
                }

                inToken = true;
                position++;
                continue;
            }

            if (ch == '}')
            {
                if (position + 1 < span.Length && span[position + 1] == '}')
                {
                    builder.Append('}');
                    position += 2;
                    continue;
                }

                if (!inToken)
                {
                    throw new FormatException($"Unmatched '}}' in cache key template '{template}'.");
                }

                var token = builder.ToString().Trim();
                if (token.Length == 0)
                {
                    throw new FormatException($"Empty token detected in cache key template '{template}'.");
                }

                segments.Add(new Segment(token, true));
                builder.Clear();
                inToken = false;
                position++;
                continue;
            }

            builder.Append(ch);
            position++;
        }

        if (inToken)
        {
            throw new FormatException($"Cache key template '{template}' ended while parsing a token.");
        }

        if (builder.Length > 0)
        {
            segments.Add(new Segment(builder.ToString(), false));
        }

        return segments.ToArray();
    }

    private static bool TryResolveValue(string token, object? entity, IReadOnlyDictionary<string, object?> ambient, out object? value)
    {
        if (ambient.TryGetValue(token, out value))
        {
            return true;
        }

        if (entity is not null)
        {
            if (TryResolveFromObject(entity, token, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryResolveFromObject(object? source, string path, out object? value)
    {
        value = null;
        if (source is null)
        {
            return false;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            value = null;
            return false;
        }

        object? current = source;
        foreach (var rawSegment in segments)
        {
            if (current is null)
            {
                value = null;
                return false;
            }

            var segment = rawSegment;
            if (string.Equals(segment, "Entity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (current is IReadOnlyDictionary<string, object?> readOnlyDict)
            {
                if (readOnlyDict.TryGetValue(segment, out var dictValue))
                {
                    current = dictValue;
                    continue;
                }
            }
            else if (current is IDictionary dictionary)
            {
                if (dictionary.Contains(segment))
                {
                    current = dictionary[segment];
                    continue;
                }
            }

            var type = current.GetType();
            var property = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy);
            if (property is null)
            {
                property = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy);
            }

            if (property is not null)
            {
                current = property.GetValue(current);
                continue;
            }

            var field = type.GetField(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy);
            if (field is not null)
            {
                current = field.GetValue(current);
                continue;
            }

            value = null;
            return false;
        }

        value = current;
        return true;
    }

    private static string? ConvertToString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is IEnumerable enumerable and not string)
        {
            var parts = new List<string>();
            foreach (var item in enumerable)
            {
                var rendered = ConvertToString(item);
                if (rendered is not null)
                {
                    parts.Add(rendered);
                }
            }

            return string.Join(',', parts);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }

    private readonly record struct Segment(string Content, bool IsPlaceholder);
}
