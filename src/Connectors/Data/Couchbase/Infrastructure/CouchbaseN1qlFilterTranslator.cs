using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Optimization;

namespace Koan.Data.Connector.Couchbase.Infrastructure;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into a parameterized N1QL WHERE clause
/// (DATA-XXXX). Replaces the legacy <c>CouchbaseLinqQueryTranslator</c>, whose collection
/// <c>Contains</c> path threw <see cref="NotSupportedException"/> with no fallback (a 500).
///
/// CONTRACT: under the new pipeline the framework's <c>FilterPushdownCoordinator</c> only ever
/// hands this adapter nodes it declared pushable in <see cref="Capabilities"/>. So this
/// translator translates the WHOLE filter it receives and NEVER throws to the caller — any
/// operator it cannot express is simply left OUT of <see cref="Capabilities"/> and the
/// in-memory floor evaluates it. The locked null/Nin semantics are honored: null/missing is
/// not a member of any set, so <see cref="FilterOperator.Nin"/>/<see cref="FilterOperator.HasNone"/>
/// match null/missing while <see cref="FilterOperator.In"/>/<see cref="FilterOperator.HasAny"/> do not;
/// relational comparisons against null are naturally false in N1QL.
/// </summary>
internal sealed class CouchbaseN1qlFilterTranslator
{
    private const string DocumentAlias = "doc";

    /// <summary>Scalar operators this adapter can translate to N1QL.</summary>
    private static readonly IReadOnlySet<FilterOperator> ScalarOps = new HashSet<FilterOperator>
    {
        FilterOperator.Eq, FilterOperator.Ne,
        FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
        FilterOperator.In, FilterOperator.Nin,
        FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
        FilterOperator.Exists,
    };

    /// <summary>
    /// Collection-containment operators on a JSON-array field (e.g. <c>List&lt;string&gt;</c>
    /// stored as a JSON array). Mapped to N1QL ANY/ARRAY_LENGTH constructs.
    /// </summary>
    private static readonly IReadOnlySet<FilterOperator> CollectionOps = new HashSet<FilterOperator>
    {
        FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll, FilterOperator.HasNone,
        FilterOperator.Size,
    };

    /// <summary>
    /// What this adapter can push down. IgnoreCase is intentionally left to the floor: N1QL
    /// case-insensitive comparison would require LOWER()/UPPER() wrapping on both sides and risk
    /// diverging from the evaluator's Ordinal-vs-OrdinalIgnoreCase oracle, so case-insensitive
    /// nodes become residual instead of being mistranslated.
    /// </summary>
    public static FilterCapabilities Capabilities { get; } =
        new(ScalarOps, CollectionOps, NestedPaths: true, IgnoreCase: false);

    private readonly StorageOptimizationInfo _optimization;
    private readonly Type _entityType;
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);

    private CouchbaseN1qlFilterTranslator(StorageOptimizationInfo optimization, Type entityType)
    {
        _optimization = optimization;
        _entityType = entityType;
    }

    /// <summary>
    /// Translates a (guaranteed-pushable) filter into a parameterized WHERE body plus its
    /// parameters. The returned clause is already fully parenthesized.
    /// </summary>
    public static CouchbaseFilterTranslation Translate(Filter filter, Type entityType, StorageOptimizationInfo optimization)
    {
        var translator = new CouchbaseN1qlFilterTranslator(optimization, entityType);
        var where = translator.Visit(filter);
        return new CouchbaseFilterTranslation(where, translator._parameters);
    }

    private string Visit(Filter filter) => filter switch
    {
        AllOf all => Combine(all.Operands, "AND"),
        AnyOf any => Combine(any.Operands, "OR"),
        Not n => $"NOT ({Visit(n.Operand)})",
        FieldFilter f => VisitField(f),
        _ => throw new NotSupportedException($"Couchbase translator received an unexpected filter node '{filter.GetType().Name}'."),
    };

    private string Combine(IReadOnlyList<Filter> operands, string op)
    {
        if (operands.Count == 0) return "TRUE";
        var sb = new StringBuilder();
        sb.Append('(');
        for (var i = 0; i < operands.Count; i++)
        {
            if (i > 0) sb.Append(' ').Append(op).Append(' ');
            sb.Append(Visit(operands[i]));
        }
        sb.Append(')');
        return sb.ToString();
    }

    private string VisitField(FieldFilter f)
    {
        var resolved = FieldPathResolver.Resolve(_entityType, f.Field);
        var field = ResolveFieldExpression(f.Field, resolved);

        if (f.Operator == FilterOperator.Exists)
        {
            var present = ScalarRaw(f.Value) as bool? ?? true;
            return present ? $"({field} IS NOT MISSING AND {field} IS NOT NULL)" : $"({field} IS MISSING OR {field} IS NULL)";
        }

        if (resolved.TargetsCollection)
            return VisitCollection(f, field, resolved.ComparableType);

        return VisitScalar(f, field, resolved.ComparableType);
    }

    private string VisitScalar(FieldFilter f, string field, Type comparable)
    {
        switch (f.Operator)
        {
            case FilterOperator.Eq:
            {
                var value = CoerceScalar(f.Value, comparable);
                return value is null ? $"({field} IS NULL)" : $"({field} = {AddParameter(value)})";
            }
            case FilterOperator.Ne:
            {
                var value = CoerceScalar(f.Value, comparable);
                // Locked equality semantics: Ne should NOT match null/missing (null is unknown,
                // not "different"); the evaluator treats null != value as false.
                return value is null ? $"({field} IS NOT NULL)" : $"({field} != {AddParameter(value)})";
            }
            case FilterOperator.Gt: return $"({field} > {AddParameter(CoerceScalar(f.Value, comparable))})";
            case FilterOperator.Gte: return $"({field} >= {AddParameter(CoerceScalar(f.Value, comparable))})";
            case FilterOperator.Lt: return $"({field} < {AddParameter(CoerceScalar(f.Value, comparable))})";
            case FilterOperator.Lte: return $"({field} <= {AddParameter(CoerceScalar(f.Value, comparable))})";
            case FilterOperator.In:
            {
                var set = CoerceSet(f.Value, comparable);
                // null is not a member of any set -> In never matches null/missing (consistent
                // with the evaluator). An empty set matches nothing.
                return $"({field} IN {AddParameter(set)})";
            }
            case FilterOperator.Nin:
            {
                var set = CoerceSet(f.Value, comparable);
                // null/missing is NOT a member of the set, so Nin MATCHES it (locked semantics).
                return $"({field} IS MISSING OR {field} IS NULL OR {field} NOT IN {AddParameter(set)})";
            }
            case FilterOperator.StartsWith:
                return $"({field} LIKE {AddParameter(ToLikePattern(ScalarString(f.Value), suffix: true))})";
            case FilterOperator.EndsWith:
                return $"({field} LIKE {AddParameter(ToLikePattern(ScalarString(f.Value), prefix: true))})";
            case FilterOperator.Contains:
                return $"({field} LIKE {AddParameter(ToLikePattern(ScalarString(f.Value), prefix: true, suffix: true))})";
            default:
                throw new NotSupportedException($"Couchbase translator received unsupported scalar operator '{f.Operator}'.");
        }
    }

    private string VisitCollection(FieldFilter f, string field, Type element)
    {
        switch (f.Operator)
        {
            case FilterOperator.Has:
            {
                var value = CoerceScalar(f.Value, element);
                return $"(ANY x IN {field} SATISFIES x = {AddParameter(value)} END)";
            }
            case FilterOperator.HasAny:
            {
                var set = CoerceSet(f.Value, element);
                return $"(ANY x IN {field} SATISFIES x IN {AddParameter(set)} END)";
            }
            case FilterOperator.HasAll:
            {
                var set = CoerceSet(f.Value, element);
                if (set.Count == 0) return "TRUE"; // superset of the empty set
                var sb = new StringBuilder();
                sb.Append('(');
                for (var i = 0; i < set.Count; i++)
                {
                    if (i > 0) sb.Append(" AND ");
                    sb.Append("ANY x IN ").Append(field)
                      .Append(" SATISFIES x = ").Append(AddParameter(set[i])).Append(" END");
                }
                sb.Append(')');
                return sb.ToString();
            }
            case FilterOperator.HasNone:
            {
                var set = CoerceSet(f.Value, element);
                // Disjoint from the set. A missing/null/empty array satisfies this (it overlaps
                // nothing), matching the evaluator's HasNone-matches-empty semantics.
                return $"(NOT ANY x IN {field} SATISFIES x IN {AddParameter(set)} END)";
            }
            case FilterOperator.Size:
            {
                var count = CoerceScalar(f.Value, typeof(int));
                return $"(ARRAY_LENGTH({field}) = {AddParameter(count)})";
            }
            default:
                throw new NotSupportedException($"Couchbase translator received unsupported collection operator '{f.Operator}'.");
        }
    }

    /// <summary>
    /// Maps a field path to its N1QL expression. The entity Id maps to <c>META().id</c>; every
    /// other path is camelCased and dotted under the document alias (nested paths supported).
    /// </summary>
    private string ResolveFieldExpression(FieldPath path, ResolvedField resolved)
    {
        if (path.Segments.Count == 1 && IsIdMember(path.Segments[0]))
            return "META().id";

        var sb = new StringBuilder(DocumentAlias);
        foreach (var segment in path.Segments)
        {
            sb.Append('.');
            sb.Append(QuoteIdentifier(NormalizeProperty(segment)));
        }
        return sb.ToString();
    }

    private string AddParameter(object? value)
    {
        var name = "$p" + _parameters.Count.ToString(CultureInfo.InvariantCulture);
        _parameters[name] = NormalizeValue(value);
        return name;
    }

    // --- value coercion helpers (route every value through FilterValueConverter) ---

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

    private static object? CoerceScalar(FilterValue v, Type t)
        => FilterValueConverter.Convert(ScalarRaw(v), t);

    private List<object?> CoerceSet(FilterValue v, Type t)
    {
        var list = new List<object?>();
        foreach (var raw in SetRaw(v))
            list.Add(NormalizeValue(FilterValueConverter.Convert(raw, t)));
        return list;
    }

    private static string ScalarString(FilterValue v)
        => Convert.ToString(ScalarRaw(v), CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Normalizes a CLR value to the on-disk JSON representation Couchbase persists. Critically,
    /// GUIDs are stored as the 32-char dashless ("N") form by <c>GetKey</c>/<c>PrepareEntityForStorage</c>
    /// — so filter parameters must use the same form or they silently never match.
    /// </summary>
    private static object? NormalizeValue(object? value) => value switch
    {
        Guid guid => guid.ToString("N", CultureInfo.InvariantCulture),
        // Enums are persisted as their NUMERIC value (the Couchbase SDK's default Newtonsoft serializer,
        // like the relational adapters, writes enums as integers — e.g. tier:1). Comparing against the
        // enum NAME ("Pro") never matched the stored number, so filter values must use the same numeric
        // form. (Caught by the FilterConvergence oracle once Couchbase became testable.)
        Enum enumeration => System.Convert.ToInt64(enumeration, CultureInfo.InvariantCulture),
        DateTime dt when dt.Kind == DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        _ => value
    };

    private static string ToLikePattern(string value, bool prefix = false, bool suffix = false)
    {
        // Escape N1QL LIKE wildcards in the literal so a user-supplied % or _ is matched literally.
        var escaped = value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var sb = new StringBuilder();
        if (prefix) sb.Append('%');
        sb.Append(escaped);
        if (suffix) sb.Append('%');
        return sb.ToString();
    }

    private bool IsIdMember(string memberName)
        => string.Equals(memberName, _optimization.IdPropertyName, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(memberName, "Id", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProperty(string property)
        => property.Length == 0 ? property : property[..1].ToLowerInvariant() + property[1..];

    private static string QuoteIdentifier(string name) => "`" + name.Replace("`", "``") + "`";
}

/// <summary>A translated N1QL WHERE body plus its named parameters.</summary>
internal readonly record struct CouchbaseFilterTranslation(string WhereClause, IReadOnlyDictionary<string, object?> Parameters);
