using Koan.Data.Abstractions;
using Koan.Data.Relational.Mapping;

namespace Koan.Data.Connector.Firebird.Runtime;

/// <summary>
/// Firebird's SQL, in the words the shared translator and schema orchestrator ask for.
///
/// <para>The shared mapping stores the whole entity as one JSON document column, and every other
/// relational dialect in the fleet lowers paths inside it with the store's JSON functions. Firebird 5
/// ships none. This store therefore mirrors every scalar document path into a plain shadow column —
/// written by the adapter beside the document on every insert, created by the DDL executor — and
/// <see cref="Read"/> resolves a single-segment path to exactly that column, so scalar filters, sorts
/// and indexes are still answered by the store. A deeper path has no column and no JSON functions
/// behind it, so it refuses by name; the adapter declares NestedPaths=false, and the coordinator never
/// sends one.</para>
/// </summary>
internal sealed class FirebirdDialect : IRelationalMappingDialect
{
    public string QuoteIdent(string ident) => Quote(ident);
    public string Parameter(int index) => $"@p{index}";

    public string EscapeLike(string fragment) => fragment.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    public string Read(PhysicalPath path, MappingValueShape shape, Type physicalType)
    {
        if (!path.IsNested) return Quote(path.Name);
        if (path.Segments.Count == 1) return Quote(path.Segments[0]);
        throw new NotSupportedException(
            $"Firebird holds no JSON functions and mirrors only top-level scalars, so physical path '{path}' " +
            "cannot be read as SQL. This adapter declares NestedPaths=false; a deeper read reaching the " +
            "dialect is a filter-planning defect.");
    }

    public string? JsonArrayOrderTerm(
        string arraySql,
        IReadOnlyList<string> elementSegments,
        bool max,
        bool descending,
        Type elementValueType) => null;

    public string JsonArrayContains(string columnSql, string parameter) =>
        throw new NotSupportedException(
            "Firebird has no JSON functions, so collection containment is not lowered to SQL. " +
            "Collection operators are excluded from this adapter's declared FilterSupport and answered by the framework floor.");

    public string JsonArrayLength(string columnSql) =>
        throw new NotSupportedException(
            "Firebird has no JSON functions, so collection size is not lowered to SQL. " +
            "Collection operators are excluded from this adapter's declared FilterSupport and answered by the framework floor.");

    public string JsonArrayElementLike(string columnSql, string patternParameter, string literalParameter) =>
        throw new NotSupportedException(
            "Firebird has no JSON functions, so element matching inside a collection is not lowered to SQL. " +
            "Collection operators are excluded from this adapter's declared FilterSupport and answered by the framework floor.");

    internal static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
