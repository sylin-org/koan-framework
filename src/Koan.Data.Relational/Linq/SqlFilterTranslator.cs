using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Relational.Mapping;

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
    private readonly MappingPlan? _mapping;
    private readonly IRelationalMappingDialect? _mappingDialect;
    private readonly Func<ResolvedField, string>? _managedResolver;
    private readonly Func<object?, object?> _encodeColumn = static value => value;
    private readonly List<object?> _parameters = new();
    // The sets of the statement being translated, in the order they are lowered, and the ones the statement budget moved
    // to one bound value or found no dialect can move (DATA-0162).
    private readonly List<SetLowering> _sets = new();
    private readonly HashSet<int> _forced = new();
    private readonly HashSet<int> _unmovable = new();

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

    /// <summary>Creates a translator whose physical paths and encodings come only from one compiled map.</summary>
    public SqlFilterTranslator(
        IRelationalMappingDialect dialect,
        MappingPlan mapping,
        Func<ResolvedField, string>? managedResolver = null)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _mappingDialect = dialect;
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _managedResolver = managedResolver;
        _entityType = mapping.EntityType;
        _columnResolver = (_, _) => throw new InvalidOperationException("Mapped translation does not use a caller-owned column resolver.");
    }

    /// <summary>
    /// Creates a mapped translator whose comparands for a real (non-nested) scalar column pass through
    /// <paramref name="encodeColumn"/>, the same function the store's write path applies to that column, so a filter
    /// compares like for like with what was written.
    /// </summary>
    public SqlFilterTranslator(
        IRelationalMappingDialect dialect,
        MappingPlan mapping,
        Func<ResolvedField, string>? managedResolver,
        Func<object?, object?> encodeColumn)
        : this(dialect, mapping, managedResolver)
        => _encodeColumn = encodeColumn ?? throw new ArgumentNullException(nameof(encodeColumn));

    /// <summary>Translates the whole filter into a WHERE fragment plus its ordered parameter values.</summary>
    public (string whereSql, IReadOnlyList<object?> parameters) Translate(Filter filter) => Translate(filter, 0);

    /// <summary>
    /// Translates the whole filter for a statement that binds <paramref name="reservedParameters"/> parameters of its own
    /// besides the filter (a conditional replace's SET values and identity), so the statement as a whole stays within
    /// the store's <see cref="ILinqSqlDialect.MaximumParameters"/> (DATA-0162).
    /// </summary>
    public (string whereSql, IReadOnlyList<object?> parameters) Translate(Filter filter, int reservedParameters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reservedParameters);
        var bound = _dialect.MaximumParameters == int.MaxValue
            ? int.MaxValue
            : Math.Max(0, _dialect.MaximumParameters - reservedParameters);
        // The parameter bound belongs to the statement. While the statement is over it, the largest set still bound a
        // parameter per member moves to one bound value, and the filter is lowered again; when no set can move and the
        // statement is still over, it is refused rather than sent to a store that would reject it (DATA-0162).
        while (true)
        {
            _parameters.Clear();
            _sets.Clear();
            var sql = Visit(filter);
            if (_parameters.Count <= bound) return (sql, _parameters);
            var movable = _sets
                .Where(set => set.PerMember && !_unmovable.Contains(set.Ordinal))
                .OrderByDescending(static set => set.Members)
                .FirstOrDefault();
            if (movable is null)
            {
                var stuck = _sets.Where(static set => set.PerMember).ToList();
                throw new NotSupportedException(
                    $"This filter binds {_parameters.Count} parameters, more than the {bound} its statement may bind on " +
                    $"this store ({_dialect.MaximumParameters} less {reservedParameters} the statement binds itself), and " +
                    "nothing more of it can bind as one value here: " +
                    (stuck.Count == 0
                        ? "every list already does, and the rest are plain comparisons"
                        : string.Join("; ", stuck.Select(static set =>
                            $"a list of {set.Members} {set.MemberTypes} members binds only one parameter per member here"))) +
                    ". Filter in smaller parts (DATA-0162).");
            }
            _forced.Add(movable.Ordinal);
        }
    }

    private sealed record SetLowering(int Ordinal, int Members, bool PerMember, string MemberTypes);

    private string Visit(Filter filter) => filter switch
    {
        AllOf all => Combine(all.Operands, "AND"),
        AnyOf any => Combine(any.Operands, "OR"),
        // Not over a field's In is that field's Nin, and the reverse: InList writes the two as exact complements under the
        // oracle's null rules, so lowering them directly keeps a large set a plain membership the store can join on
        // instead of a per-row CASE (DATA-0162).
        Not { Operand: FieldFilter { Operator: FilterOperator.In or FilterOperator.Nin } f } =>
            VisitField(f with { Operator = f.Operator == FilterOperator.In ? FilterOperator.Nin : FilterOperator.In }),
        Not n => Negate(Visit(n.Operand)),
        FieldFilter f => VisitField(f),
        // ClrFilter is never pushable; the coordinator keeps it out of what we receive.
        _ => throw new NotSupportedException($"Filter node '{filter.GetType().Name}' is not translatable to SQL.")
    };

    /// <summary>
    /// The oracle (<see cref="InMemoryFilterEvaluator"/>) is two-valued: a predicate over a null or missing field is
    /// false, so its negation is true. SQL is three-valued, and <c>NOT</c> of unknown stays unknown and drops the row.
    /// <c>CASE</c> folds unknown to false before the negation, in a form every relational dialect Koan ships accepts.
    /// </summary>
    private static string Negate(string predicate) => $"(CASE WHEN {predicate} THEN 1 ELSE 0 END = 0)";

    private string Combine(IReadOnlyList<Filter> operands, string op)
    {
        if (operands.Count == 0) return op == "AND" ? "1=1" : "1=0";
        if (operands.Count == 1) return Visit(operands[0]);
        return "(" + string.Join($" {op} ", operands.Select(Visit)) + ")";
    }

    private string VisitField(FieldFilter f)
    {
        var resolved = FieldPathResolver.Resolve(_entityType, f.Field);
        // JSON filters bind member names case-insensitively, but relational stores extract values from
        // case-sensitive JSON paths. Hand adapters the canonical resolved member path rather than the
        // caller's spelling (for example, `subject` resolves to `Subject`). Manually-built filters from
        // older callers remain compatible because CanonicalPath is optional on ResolvedField.
        MappingBindingPlan? mapped = null;
        string column;
        if (_mapping is not null)
        {
            if (resolved.IsManaged)
            {
                column = _managedResolver?.Invoke(resolved)
                    ?? throw new MappingValueException(
                        _mapping.Id,
                        resolved.StorageName ?? f.Field.ToString(),
                        "This relational mapping has no physical binding for framework-managed fields.");
            }
            else
            {
                var use = _mapping.Use(resolved.CanonicalPath ?? f.Field, MappingConsumer.Filter);
                mapped = use.Bindings.Single();
                column = _mappingDialect!.Read(mapped.PhysicalPath, mapped.Shape, mapped.PhysicalType);
                if (mapped is { PhysicalPath.IsNested: true, Shape: MappingValueShape.Scalar })
                    column = _mappingDialect.DocumentOperand(column, mapped.PhysicalType);
            }
        }
        else column = _columnResolver(resolved.CanonicalPath ?? f.Field, resolved);

        if (resolved.TargetsCollection)
            return VisitCollection(f, resolved, column);

        return VisitScalar(f, resolved, column, mapped);
    }

    private string VisitScalar(FieldFilter f, ResolvedField field, string column, MappingBindingPlan? mapped)
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
                return $"({column} = {AddParam(raw, mapped)})";
            }
            case FilterOperator.Ne:
            {
                var raw = ScalarValue(f.Value, field.ComparableType);
                // The oracle's Ne is !Eq. Ne null -> "is not null"; Ne v is true for a null or missing value, which
                // SQL <> alone would drop as unknown.
                if (raw is null) return $"({column} IS NOT NULL)";
                return $"({column} IS NULL OR {column} <> {AddParam(raw, mapped)})";
            }
            case FilterOperator.Gt: return Compare(column, ">", f.Value, field.ComparableType, mapped);
            case FilterOperator.Gte: return Compare(column, ">=", f.Value, field.ComparableType, mapped);
            case FilterOperator.Lt: return Compare(column, "<", f.Value, field.ComparableType, mapped);
            case FilterOperator.Lte: return Compare(column, "<=", f.Value, field.ComparableType, mapped);
            case FilterOperator.StartsWith:
            case FilterOperator.EndsWith:
            case FilterOperator.Contains:
            {
                var raw = ScalarValue(f.Value, typeof(string)) as string ?? string.Empty;
                return _dialect.StringMatch(column, f.Operator, raw, value => AddParam(value, mapped));
            }
            case FilterOperator.In: return InList(column, f, field, mapped, negate: false);
            case FilterOperator.Nin: return InList(column, f, field, mapped, negate: true);
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
                var single = ElementComparand(ScalarValue(f.Value, elementType), elementType);
                return _dialect.JsonArrayContains(column, AddParam(single), elementType);
            }
            case FilterOperator.HasAny:
            {
                var members = SetValues(f.Value, elementType).Select(v => ElementComparand(v, elementType)).ToList();
                if (members.Count == 0) return "1=0";
                return CollectionSet(column, members, elementType, SetMatch.Any,
                    preds => Disjoin(preds, emptyResult: "1=0"));
            }
            case FilterOperator.HasAll:
            {
                var members = SetValues(f.Value, elementType).Select(v => ElementComparand(v, elementType)).ToList();
                if (members.Count == 0) return "1=1";
                return CollectionSet(column, members, elementType, SetMatch.All,
                    preds => Conjoin(preds, emptyResult: "1=1"));
            }
            case FilterOperator.HasNone:
            {
                // null/empty collection is disjoint from any set -> HasNone matches (locked semantics).
                var members = SetValues(f.Value, elementType).Select(v => ElementComparand(v, elementType)).ToList();
                if (members.Count == 0) return "1=1";
                return CollectionSet(column, members, elementType, SetMatch.None,
                    preds => $"(NOT {Disjoin(preds, emptyResult: "1=0")})");
            }
            case FilterOperator.HasContains:
            {
                // Some string element contains the value. Both forms are bound: the LIKE pattern
                // (escaped exactly like the scalar Contains path, so metacharacters match literally)
                // and the raw literal for dialects that match literally to keep the store's answer
                // case-sensitive like the floor. Null/missing/empty arrays answer false in every emit.
                var raw = ScalarValue(f.Value, typeof(string)) as string ?? string.Empty;
                var pattern = "%" + _dialect.EscapeLike(raw) + "%";
                return _dialect.JsonArrayElementLike(column, AddParam(pattern), AddParam(raw));
            }
            default:
                throw new NotSupportedException($"Operator '{f.Operator}' is not valid on collection field '{f.Field}'.");
        }
    }

    private string Compare(string column, string op, FilterValue value, Type comparable, MappingBindingPlan? mapped)
    {
        var raw = ScalarValue(value, comparable);
        // Comparisons with null are false (locked semantics); SQL 3-valued logic already drops NULL rows
        // from a positive predicate, so emit an always-false clause for a null operand.
        if (raw is null) return "1=0";
        if (raw is Enum enumeration && mapped?.Descriptor.Codec is null)
        {
            _ = EnumStorageEncoding.Format(enumeration);
            return $"({RelationalEnumOrder.Rank(column, enumeration.GetType())} {op} {AddParam(Convert.ToDecimal(enumeration, CultureInfo.InvariantCulture))})";
        }
        return $"({column} {op} {AddParam(raw, mapped)})";
    }

    private string InList(string column, FieldFilter f, ResolvedField field, MappingBindingPlan? mapped, bool negate)
    {
        var values = SetValues(f.Value, field.ComparableType).ToList();
        if (values.Count == 0)
            // "in empty set" matches nothing; "not in empty set" matches everything (incl. null).
            return negate ? "1=1" : "1=0";

        // The oracle's In asks Eq of each member, so a null member matches a null or missing value; Nin is !In.
        // SQL IN never matches NULL, and NOT IN over a list holding NULL is never true, so null is split out.
        var members = values.Where(static value => value is not null).ToList();
        var holdsNull = members.Count != values.Count;
        if (members.Count == 0)
            return negate ? $"({column} IS NOT NULL)" : $"({column} IS NULL)";

        var membership = Membership(column, members.Select(value => Encode(value, mapped)).ToList(), negate);
        if (!negate)
            return holdsNull
                ? $"({column} IS NULL OR {membership})"
                : $"({membership})";

        return holdsNull
            ? $"({column} IS NOT NULL AND {membership})"
            : $"({column} IS NULL OR {membership})";
    }

    /// <summary>
    /// "<paramref name="column"/> is (or, negated, is not) one of <paramref name="encoded"/>", unparenthesized: one bound
    /// value for the whole set where the dialect carries one, else one parameter per member in IN lists the dialect's
    /// bound allows (DATA-0162).
    /// </summary>
    private string Membership(string column, IReadOnlyList<object?> encoded, bool negate)
    {
        var ordinal = _sets.Count;
        var forced = _forced.Contains(ordinal);
        if ((forced || !_dialect.PrefersSetParameters) && !_unmovable.Contains(ordinal))
        {
            if (_dialect.MemberOf(column, encoded, negate, Bind) is { } set)
            {
                _sets.Add(new SetLowering(ordinal, encoded.Count, PerMember: false, MemberTypes(encoded)));
                return set;
            }
            if (forced) _unmovable.Add(ordinal);
        }
        _sets.Add(new SetLowering(ordinal, encoded.Count, PerMember: true, MemberTypes(encoded)));
        var keyword = negate ? "NOT IN" : "IN";
        var lists = encoded
            .Chunk(Math.Max(1, _dialect.MaximumInListMembers))
            .Select(chunk => $"{column} {keyword} ({string.Join(", ", chunk.Select(Bind))})")
            .ToList();
        return lists.Count == 1
            ? lists[0]
            : "(" + string.Join(negate ? " AND " : " OR ", lists) + ")";
    }

    /// <summary>
    /// A JSON array column against a list of members (HasAny, HasAll, HasNone): one bound value through the dialect's
    /// <see cref="ILinqSqlDialect.CollectionMemberOf"/> wherever it carries one — even where short IN lists are preferred,
    /// because the per-member form reads the array once per member — else one
    /// <see cref="ILinqSqlDialect.JsonArrayContains(string, string, Type)"/> per member joined by
    /// <paramref name="perMember"/>, which stays within the statement's bound or is refused with it (DATA-0162).
    /// </summary>
    private string CollectionSet(string column, IReadOnlyList<object?> encoded, Type elementType, SetMatch match,
        Func<List<string>, string> perMember)
    {
        var ordinal = _sets.Count;
        if (!_unmovable.Contains(ordinal))
        {
            if (_dialect.CollectionMemberOf(column, encoded, match, elementType, Bind) is { } set)
            {
                _sets.Add(new SetLowering(ordinal, encoded.Count, PerMember: false, MemberTypes(encoded)));
                return set;
            }
            _unmovable.Add(ordinal);
        }
        _sets.Add(new SetLowering(ordinal, encoded.Count, PerMember: true, MemberTypes(encoded)));
        return perMember(encoded.Select(member => _dialect.JsonArrayContains(column, Bind(member), elementType)).ToList());
    }

    // An element of a JSON array is compared as a scalar read out of a document is: the comparand in the canonical store
    // form (DATA-0100), then in the form the dialect reads the element as (DATA-0139). Has and the list operators share it,
    // so a list answers what its members answer one by one.
    private object? ElementComparand(object? value, Type elementType) =>
        _dialect.JsonArrayComparand(ComparableScalarEncoding.EncodeComparand(value), elementType);

    private static string MemberTypes(IReadOnlyList<object?> encoded) =>
        string.Join("/", encoded.Select(static member => member?.GetType().Name ?? "null").Distinct().Take(3));

    private static string Disjoin(List<string> preds, string emptyResult)
        => preds.Count == 0 ? emptyResult : preds.Count == 1 ? preds[0] : "(" + string.Join(" OR ", preds) + ")";

    private static string Conjoin(List<string> preds, string emptyResult)
        => preds.Count == 0 ? emptyResult : preds.Count == 1 ? preds[0] : "(" + string.Join(" AND ", preds) + ")";

    private string AddParam(object? value, MappingBindingPlan? mapped = null) => Bind(Encode(value, mapped));

    private string Bind(object? encoded)
    {
        var idx = _parameters.Count;
        _parameters.Add(encoded);
        return _dialect.Parameter(idx);
    }

    private object? Encode(object? value, MappingBindingPlan? mapped)
    {
        // Comparable-encoding contract (DATA-0100): encode the comparand to the SAME canonical store form
        // the write path produces (DateTimeOffset -> UTC-ISO text, TimeSpan -> ticks, DateOnly/TimeOnly ->
        // fixed text), so a pushed comparison is like-for-like. The ADO.NET drivers otherwise bind these
        // CLR types in a form that does not match the stored JSON text (or cannot bind them at all).
        // A real scalar column holds whatever the store's write path encoded into it, so its comparand takes the
        // same encoder (DATA-0136). Non-governed types pass through unchanged.
        // A scalar read out of a JSON document is compared as the dialect's document text (DATA-0139).
        var encoded = mapped is null ? value : mapped.Encode(value);
        return mapped switch
        {
            { PhysicalPath.IsNested: false, Shape: MappingValueShape.Scalar } => _encodeColumn(encoded),
            { PhysicalPath.IsNested: true, Shape: MappingValueShape.Scalar } when _mappingDialect is not null =>
                _mappingDialect.DocumentComparand(ComparableScalarEncoding.EncodeComparand(encoded), mapped.PhysicalType),
            _ => ComparableScalarEncoding.EncodeComparand(encoded)
        };
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
