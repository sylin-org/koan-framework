using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Core.Sorting;

/// <summary>
/// Parses string sort grammar (URL/CLI/config form) into structured <see cref="SortSpec"/> lists.
/// Grammar: comma-separated fields, optional leading <c>+</c> (asc) or <c>-</c> (desc), dot-paths supported.
/// </summary>
public static class SortSpecParser
{
    public readonly record struct ParseResult(IReadOnlyList<SortSpec> Specs, IReadOnlyList<string> SkippedFields);

    /// <summary>Parses strictly: unresolvable fields throw <see cref="InvalidSortFieldException"/>.</summary>
    public static IReadOnlyList<SortSpec> ParseStrict<T>(string? expression)
        => ParseStrict(typeof(T), expression);

    /// <summary>Parses strictly: unresolvable fields throw <see cref="InvalidSortFieldException"/>.</summary>
    public static IReadOnlyList<SortSpec> ParseStrict(Type rootType, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return Array.Empty<SortSpec>();

        var specs = new List<SortSpec>();
        foreach (var raw in expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (desc, field) = SplitDirection(raw);
            if (string.IsNullOrEmpty(field))
                throw new InvalidSortFieldException(raw, rootType, string.Empty, "Empty field after direction prefix.");

            var path = MemberPathResolver.ResolveStrict(rootType, field);
            specs.Add(Build(path, desc));
        }
        return specs;
    }

    /// <summary>Parses leniently: unresolvable fields are collected into <see cref="ParseResult.SkippedFields"/> instead of throwing.</summary>
    public static ParseResult ParseLenient<T>(string? expression)
        => ParseLenient(typeof(T), expression);

    /// <summary>Parses leniently: unresolvable fields are collected into <see cref="ParseResult.SkippedFields"/> instead of throwing.</summary>
    public static ParseResult ParseLenient(Type rootType, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return new ParseResult(Array.Empty<SortSpec>(), Array.Empty<string>());

        var specs = new List<SortSpec>();
        var skipped = new List<string>();
        foreach (var raw in expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (desc, field) = SplitDirection(raw);
            if (string.IsNullOrEmpty(field))
            {
                skipped.Add(raw);
                continue;
            }
            try
            {
                var path = MemberPathResolver.ResolveStrict(rootType, field);
                specs.Add(Build(path, desc));
            }
            catch (InvalidSortFieldException)
            {
                skipped.Add(raw);
            }
        }
        return new ParseResult(specs, skipped);
    }

    /// <summary>Parses a single token (no commas) strictly. Convenience for hook helpers.</summary>
    public static SortSpec ParseSingleStrict(Type rootType, string token)
    {
        var (desc, field) = SplitDirection(token);
        if (string.IsNullOrEmpty(field))
            throw new InvalidSortFieldException(token, rootType, string.Empty, "Empty field after direction prefix.");
        var path = MemberPathResolver.ResolveStrict(rootType, field);
        return Build(path, desc);
    }

    private static (bool desc, string field) SplitDirection(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (false, string.Empty);
        return raw[0] switch
        {
            '-' => (true, raw[1..].Trim()),
            '+' => (false, raw[1..].Trim()),
            _ => (false, raw.Trim())
        };
    }

    public static SortSpec Build(MemberPath path, bool desc)
    {
        var aggregation = path.TraversesCollection
            ? (desc ? SortAggregation.Max : SortAggregation.Min)
            : SortAggregation.None;
        return new SortSpec(path, desc, aggregation);
    }
}
