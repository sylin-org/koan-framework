using System.Collections.Generic;
using System;
using System.Linq;
using Koan.Context.Models;

namespace Koan.Context.Utilities;

/// <summary>
/// Parses simple YAML-style frontmatter blocks for metadata and tags.
/// </summary>
public static class FrontmatterParser
{
    public static FrontmatterParseResult Parse(string content)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<string>();

        if (string.IsNullOrWhiteSpace(content))
        {
            return new FrontmatterParseResult(metadata, Array.Empty<string>());
        }

        var normalized = content.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---", StringComparison.Ordinal))
        {
            return new FrontmatterParseResult(metadata, Array.Empty<string>());
        }

        var blockStart = normalized.IndexOf('\n');
        if (blockStart < 0)
        {
            return new FrontmatterParseResult(metadata, Array.Empty<string>());
        }

        var blockEnd = normalized.IndexOf("\n---", blockStart + 1, StringComparison.Ordinal);
        if (blockEnd < 0)
        {
            return new FrontmatterParseResult(metadata, Array.Empty<string>());
        }

        var frontmatterBlock = normalized.Substring(blockStart + 1, blockEnd - (blockStart + 1));
        var lines = frontmatterBlock.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();

            if (key.Equals("tags", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(value))
                {
                    i = ParseMultilineTags(lines, i + 1, tags);
                }
                else
                {
                    tags.AddRange(ParseInlineTags(value));
                }

                continue;
            }

            if (!string.IsNullOrEmpty(key) && !metadata.ContainsKey(key))
            {
                metadata[key] = value.Trim('"', '\'');
            }
        }

        return new FrontmatterParseResult(
            metadata,
            TagEnvelope.NormalizeTags(tags).ToArray());
    }

    private static IEnumerable<string> ParseInlineTags(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim('"', '\''))
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private static int ParseMultilineTags(string[] lines, int startIndex, ICollection<string> target)
    {
        var index = startIndex;

        while (index < lines.Length)
        {
            var candidate = lines[index].Trim();
            if (!candidate.StartsWith("-", StringComparison.Ordinal))
            {
                return index - 1;
            }

            var tag = candidate[1..].Trim().Trim('"', '\'');
            if (!string.IsNullOrEmpty(tag))
            {
                target.Add(tag);
            }

            index++;
        }

        return lines.Length - 1;
    }
}

/// <summary>
/// Result of parsing frontmatter.
/// </summary>
public readonly record struct FrontmatterParseResult(
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<string> Tags);
