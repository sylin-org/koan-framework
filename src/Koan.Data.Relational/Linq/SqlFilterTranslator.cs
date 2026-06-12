using System.Globalization;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Relational.Linq;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into a parameterized SQL WHERE clause for a
/// relational adapter, parameterized by an <see cref="ILinqSqlDialect"/> (DATA-XXXX).
///
/// Replaces the legacy LINQ-Expression-walking <c>LinqWhereTranslator</c>: the front-end now lowers
/// LINQ and the JSON DSL into the provider-agnostic <see cref="Filter"/> model, and this class lowers
/// that model onto SQL. The framework's <c>FilterPushdownCoordinator</c> guarantees the filter handed
/// here contains <b>only</b> nodes the adapter declared pushable in its <see cref="FilterSupport"/>,
/// so this translator never computes a residual and never sees an un-pushable operator — it translates
/// the whole tree.
///
/// Column references are produced by a caller-supplied resolver so the adapter keeps ownership of its
/// JSON-extraction / projected-column mapping (the same logic its raw-SQL path already uses).
/// </summary>
public sealed class SqlFilterTranslator
{
    private readonly ILinqSqlDialect _dialect;
    private readonly Type _entityType;
    private readonly Func<FieldPath, ResolvedField, string> _columnResolver;
    private readonly List<object?> _parameters = new();

    /// <param name="dialect">Provider dialect for quoting, LIKE escaping, parameters, JSON-array containment.</param>
    /// <param name="entityType">Concrete entity type used to resolve field paths to leaf types.</param>
    /// <param name="columnResolver">
    /// Maps a resolved field to the SQL column expression that reads its value (e.g. a projected column
    /// or a <c>json_extract(...)</c>). The translator stays agnostic to the storage shape.
    /// </param>
    public SqlFilterTranslator(ILinqSqlDialect dialect, Type entityType, Func<FieldPath, ResolvedField, string> columnResolver)
    {
        _dialect = dialect;
        _entityType = entityType;
        _columnResolver = columnResolver;
    }

    /// <summary>Translates the whole filter into a WHERE fragment plus its ordered parameter values.</summary>
    public (string whereSql, IReadOnlyList<object?> parameters) Translate(Filter filter)
    {
        var sql = Visit(filter);
        return (sql, _parameters);
    }

    private string Visit(Filter filter) => filter switch
    {
        AllOf all => Combine(all.Operands, "AND"),
        AnyOf any => Combine(any.Operands, "OR"),
        Not n => $"(NOT {Visit(n.Operand)})",
        FieldFilter f => VisitField(f),
        // ClrFilter is never pushable; the coordinator keeps it out of what we receive.
        _ => throw new NotSupportedException($"Filter node '{filter.GetType().Name}' is not translatable to SQL.")
    };

    private string Combine(IReadOnlyList<Filter> operands, string op)
    {
        if (operands.Count == 0) return op == "AND" ? "1=1" : "1=0";
        if (operands.Count == 1) return Visit(operands[0]);
        return "(" + string.Join($" {op} ", operands.Select(Visit)) + ")";
    }

    private string VisitField(FieldFilter f)
    {
        var resolved = FieldPathResolver.Resolve(_entityType, f.Field);
        var column = _columnResolver(f.Field, resolved);

        if (resolved.TargetsCollection)
            return VisitCollection(f, resolved, column);

        return VisitScalar(f, resolved, column);
    }

    private string VisitScalar(FieldFilter f, ResolvedField field, string column)
    {
        switch (f.Operator)
        {
            case FilterOperator.Exists:
            {
                var present = ScalarBool(f.Value, defaultValue: true);
                return present ? $"({column} IS NOT NULL)" : $"({column} IS NULL)";
            }
            case FilterOperator.Eq:
            {
                var raw = ScalarValue(f.Value, field.ComparableType);
                if (raw is null) return $"({column} IS NULL)";
                return $"({column} = {AddParam(raw)})";
            }
            case FilterOperator.Ne:
            {
                var raw = ScalarValue(f.Value, field.ComparableType);
                // Locked semantics: comparisons treat null specially. Ne null -> "is not null".
                if (raw is null) return $"({column} IS NOT NULL)";
                // SQL <> drops NULL rows; the floor also reports Ne false when the value is null, so this matches.
                return $"({column} <> {AddParam(raw)})";
            }
            case FilterOperator.Gt: return Compare(column, ">", f.Value, field.ComparableType);
            case FilterOperator.Gte: return Compare(column, ">=", f.Value, field.ComparableType);
            case FilterOperator.Lt: return Compare(column, "<", f.Value, field.ComparableType);
            case FilterOperator.Lte: return Compare(column, "<=", f.Value, field.ComparableType);
            case FilterOperator.StartsWith: return Like(column, f, suffix: "%");
            case FilterOperator.EndsWith: return Like(column, f, prefix: "%");
            case FilterOperator.Contains: return Like(column, f, prefix: "%", suffix: "%");
            case FilterOperator.In: return InList(column, f, field, negate: false);
            case FilterOperator.Nin: return InList(column, f, field, negate: true);
            default:
                throw new NotSupportedException($"Operator '{f.Operator}' is not valid on scalar field '{f.Field}'.");
        }
    }

    private string VisitCollection(FieldFilter f, ResolvedField field, string column)
    {
        var elementType = field.ComparableType;
        switch (f.Operator)
        {
            case FilterOperator.Size:
            {
                var count = ScalarValue(f.Value, typeof(int));
                return $"({_dialect.JsonArrayLength(column)} = {AddParam(count)})";
            }
            case FilterOperator.Has:
            {
                var single = ScalarValue(f.Value, elementType);
                return _dialect.JsonArrayContains(column, AddParam(single));
            }
            case FilterOperator.HasAny:
            {
                var preds = SetValues(f.Value, elementType)
                    .Select(v => _dialect.JsonArrayContains(column, AddParam(v)))
                    .ToList();
                return Disjoin(preds, emptyResult: "1=0");
            }
            case FilterOperator.HasAll:
            {
                var preds = SetValues(f.Value, elementType)
                    .Select(v => _dialect.JsonArrayContains(column, AddParam(v)))
                    .ToList();
                return Conjoin(preds, emptyResult: "1=1");
            }
            case FilterOperator.HasNone:
            {
                var preds = SetValues(f.Value, elementType)
                    .Select(v => _dialect.JsonArrayContains(column, AddParam(v)))
                    .ToList();
                // null/empty collection is disjoint from any set -> HasNone matches (locked semantics).
                if (preds.Count == 0) return "1=1";
                return $"(NOT {Disjoin(preds, emptyResult: "1=0")})";
            }
            default:
                throw new NotSupportedException($"Operator '{f.Operator}' is not valid on collection field '{f.Field}'.");
        }
    }

    private string Compare(string column, string op, FilterValue value, Type comparable)
    {
        var raw = ScalarValue(value, comparable);
        // Comparisons with null are false (locked semantics); SQL 3-valued logic already drops NULL rows
        // from a positive predicate, so emit an always-false clause for a null operand.
        if (raw is null) return "1=0";
        return $"({column} {op} {AddParam(raw)})";
    }

    private string Like(string column, FieldFilter f, string prefix = "", string suffix = "")
    {
        var raw = ScalarValue(f.Value, typeof(string)) as string ?? string.Empty;
        var escaped = _dialect.EscapeLike(raw);
        var pattern = prefix + escaped + suffix;
        return $"({column} LIKE {AddParam(pattern)} ESCAPE '\\')";
    }

    private string InList(string column, FieldFilter f, ResolvedField field, bool negate)
    {
        var values = SetValues(f.Value, field.ComparableType).ToList();
        if (values.Count == 0)
            // "in empty set" matches nothing; "not in empty set" matches everything (incl. null).
            return negate ? "1=1" : "1=0";

        var placeholders = string.Join(", ", values.Select(AddParam));
        if (!negate)
            return $"({column} IN ({placeholders}))";

        // Locked semantics: null is not a member of any set, so Nin MATCHES null/missing.
        return $"({column} IS NULL OR {column} NOT IN ({placeholders}))";
    }

    private static string Disjoin(List<string> preds, string emptyResult)
        => preds.Count == 0 ? emptyResult : preds.Count == 1 ? preds[0] : "(" + string.Join(" OR ", preds) + ")";

    private static string Conjoin(List<string> preds, string emptyResult)
        => preds.Count == 0 ? emptyResult : preds.Count == 1 ? preds[0] : "(" + string.Join(" AND ", preds) + ")";

    private string AddParam(object? value)
    {
        var idx = _parameters.Count;
        // Comparable-encoding contract (DATA-0100): encode the comparand to the SAME canonical store form
        // the write path produces (DateTimeOffset -> UTC-ISO text, TimeSpan -> ticks, DateOnly/TimeOnly ->
        // fixed text), so a pushed comparison is like-for-like. The ADO.NET drivers otherwise bind these
        // CLR types in a form that does not match the stored JSON text (or cannot bind them at all).
        // Non-governed types pass through unchanged.
        _parameters.Add(ComparableScalarEncoding.EncodeComparand(value));
        return _dialect.Parameter(idx);
    }

    private static object? ScalarValue(FilterValue value, Type targetType)
        => FilterValueConverter.Convert(ScalarRaw(value), targetType);

    private static IEnumerable<object?> SetValues(FilterValue value, Type targetType)
        => SetRaw(value).Select(v => FilterValueConverter.Convert(v, targetType));

    private static bool ScalarBool(FilterValue value, bool defaultValue)
    {
        var raw = ScalarRaw(value);
        return raw switch
        {
            null => defaultValue,
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static object? ScalarRaw(FilterValue v) => v switch
    {
        FilterValue.Scalar s => s.Value,
        FilterValue.Set st => st.Values.Count > 0 ? st.Values[0] : null,
        _ => null
    };

    private static IReadOnlyList<object?> SetRaw(FilterValue v) => v switch
    {
        FilterValue.Set st => st.Values,
        FilterValue.Scalar s => new[] { s.Value },
        _ => Array.Empty<object?>()
    };
}
