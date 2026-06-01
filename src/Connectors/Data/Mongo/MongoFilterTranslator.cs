using System.Text.RegularExpressions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Optimization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// Translates the unified <see cref="Filter"/> AST into a MongoDB.Driver
/// <see cref="FilterDefinition{TEntity}"/> (DATA-XXXX). The coordinator only ever hands this
/// translator nodes it declared pushable in <see cref="Capabilities"/>, so it translates the
/// WHOLE filter it receives — it never computes a residual and never falls back.
///
/// Locked null/Nin semantics (DATA-XXXX §7): null/absent is not a member of any set, so
/// <c>Nin</c>/<c>HasNone</c> must MATCH missing/null documents. Mongo's <c>$nin</c> already
/// matches missing fields, but we OR in an explicit existence/null check to be robust.
/// Relational comparisons map straight to <c>$gt/$gte/$lt/$lte</c>.
///
/// GUID handling: a scalar equality/membership value that is a Guid OR a Guid-PARSEABLE string is
/// emitted as native UUID BinData — via the shared <see cref="MongoGuidEncoding"/> rule that
/// SmartStringGuidSerializer also uses on write, so Guid scalars AND Guid-shaped string fields (ids,
/// FKs) match their BinData storage and hit the UUID index. (Keying off the declared comparable type
/// alone silently broke predicate queries on string-typed Guid FKs — DATA-XXXX.) Collection (List&lt;string&gt;) element
/// values are emitted as their native CLR type (string stays a BSON string), because the array
/// element serializer is carved out of the smart serializer (see MongoOptimizationAutoRegistrar).
/// </summary>
internal sealed class MongoFilterTranslator<TEntity>
{
    private readonly Func<string, string> _mapFieldName;

    public MongoFilterTranslator(Func<string, string> mapFieldName)
        => _mapFieldName = mapFieldName;

    /// <summary>
    /// Operators this adapter pushes server-side. Anything omitted is evaluated by the in-memory
    /// floor via the coordinator. Mongo translates all scalar comparison/membership/string operators
    /// plus all collection-containment operators against native array/BSON semantics.
    /// </summary>
    public static FilterCapabilities Capabilities { get; } = new(
        ScalarOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Eq, FilterOperator.Ne,
            FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte,
            FilterOperator.In, FilterOperator.Nin,
            FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Contains,
            FilterOperator.Exists,
        },
        CollectionOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Has, FilterOperator.HasAny, FilterOperator.HasAll,
            FilterOperator.HasNone, FilterOperator.Size,
        },
        NestedPaths: true,
        IgnoreCase: true);

    public FilterDefinition<TEntity> Translate(Filter filter, Type entityType)
        => Build(filter, entityType);

    private FilterDefinition<TEntity> Build(Filter filter, Type entityType)
    {
        var b = Builders<TEntity>.Filter;
        switch (filter)
        {
            case AllOf all:
                return all.Operands.Count == 0 ? b.Empty : b.And(all.Operands.Select(o => Build(o, entityType)));
            case AnyOf any:
                return any.Operands.Count == 0 ? b.Empty : b.Or(any.Operands.Select(o => Build(o, entityType)));
            case Not n:
                return b.Not(Build(n.Operand, entityType));
            case FieldFilter f:
                return BuildField(f, FieldPathResolver.Resolve(entityType, f.Field));
            case ClrFilter:
                throw new NotSupportedException("ClrFilter is never pushable to Mongo; it must be evaluated in memory.");
            default:
                throw new NotSupportedException($"Unknown filter node '{filter.GetType().Name}'.");
        }
    }

    private FilterDefinition<TEntity> BuildField(FieldFilter f, ResolvedField field)
    {
        var b = Builders<TEntity>.Filter;
        var name = MapPath(f.Field);
        var op = f.Operator;

        if (op == FilterOperator.Exists)
        {
            var desired = ScalarRaw(f.Value) as bool? ?? true;
            return desired
                ? b.And(b.Exists(name, true), b.Ne(name, BsonNull.Value))
                : b.Or(b.Exists(name, false), b.Eq(name, BsonNull.Value));
        }

        if (field.TargetsCollection)
            return BuildCollection(op, name, f, field, b);

        // Scalar comparisons emit a raw BsonDocument (see Doc) so the value lands as a top-level
        // BsonValue rather than being wrapped by ObjectSerializer in a {_v: ...} envelope (DATA-XXXX).
        // Whether the value is GUID-encoded (string -> UUID BinData) is decided per FIELD from static
        // metadata (IdentityEncoding — the same selection the write serializer uses), never by sniffing
        // the value, so the write and query encodings cannot drift.
        var guidEncoded = IsGuidEncodedField(field);
        return op switch
        {
            FilterOperator.Eq => Doc(name, ScalarBson(f, field, guidEncoded)),
            FilterOperator.Ne => Doc(name, new BsonDocument("$ne", ScalarBson(f, field, guidEncoded))),
            FilterOperator.Gt => Doc(name, new BsonDocument("$gt", ScalarBson(f, field, guidEncoded))),
            FilterOperator.Gte => Doc(name, new BsonDocument("$gte", ScalarBson(f, field, guidEncoded))),
            FilterOperator.Lt => Doc(name, new BsonDocument("$lt", ScalarBson(f, field, guidEncoded))),
            FilterOperator.Lte => Doc(name, new BsonDocument("$lte", ScalarBson(f, field, guidEncoded))),
            FilterOperator.In => Doc(name, new BsonDocument("$in", new BsonArray(SetBson(f, field, guidEncoded)))),
            // Nin must match missing/null too (locked semantics).
            FilterOperator.Nin => b.Or(Doc(name, new BsonDocument("$nin", new BsonArray(SetBson(f, field, guidEncoded)))), b.Exists(name, false), Doc(name, BsonNull.Value)),
            FilterOperator.StartsWith => b.Regex(name, Anchored(StringScalar(f), f.IgnoreCase, AnchorMode.Prefix)),
            FilterOperator.EndsWith => b.Regex(name, Anchored(StringScalar(f), f.IgnoreCase, AnchorMode.Suffix)),
            FilterOperator.Contains => b.Regex(name, Anchored(StringScalar(f), f.IgnoreCase, AnchorMode.None)),
            _ => throw new NotSupportedException($"Operator '{op}' is not valid on scalar field '{f.Field}'.")
        };
    }

    private FilterDefinition<TEntity> BuildCollection(
        FilterOperator op, string name, FieldFilter f, ResolvedField field, FilterDefinitionBuilder<TEntity> b)
    {
        // The array field itself is targeted by name; element comparisons are over the element type (object).
        var arrayField = new StringFieldDefinition<TEntity, object>(name);
        return op switch
        {
            // Array-contains is plain equality on the array field in Mongo.
            FilterOperator.Has => b.Eq(arrayField, ElementValue(ScalarRaw(f.Value), field)),
            // $in on an array field matches when any element overlaps the set.
            FilterOperator.HasAny => b.In(arrayField, ElementSet(f, field)),
            FilterOperator.HasAll => b.All(new StringFieldDefinition<TEntity, IEnumerable<object>>(name), ElementSet(f, field)),
            // Disjoint from the set; null/missing array is disjoint -> matches (locked semantics).
            FilterOperator.HasNone => b.Or(b.Not(b.In(arrayField, ElementSet(f, field))), b.Exists(name, false)),
            FilterOperator.Size => b.Size(name, System.Convert.ToInt32(ScalarRaw(f.Value))),
            _ => throw new NotSupportedException($"Operator '{op}' is not valid on collection field '{f.Field}'.")
        };
    }

    // --- field name / path mapping ---

    private string MapPath(FieldPath path)
        => path.Segments.Count == 1
            ? _mapFieldName(path.Segments[0])
            : string.Join('.', path.Segments.Select((s, i) => i == 0 ? _mapFieldName(s) : ToCamel(s)));

    private static string ToCamel(string v)
        => string.IsNullOrEmpty(v) || !char.IsUpper(v[0]) ? v
           : v.Length == 1 ? v.ToLowerInvariant() : char.ToLowerInvariant(v[0]) + v.Substring(1);

    // --- value coercion ---

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

    // DATA-0098: a field is GUID-encoded iff IdentityEncoding says so (the entity's Id, or a declared
    // parent reference to a GUID-identity entity) — the SAME selection the write serializer uses. Only
    // top-level scalar fields are encoded here; nested paths fall through as plain values.
    private static bool IsGuidEncodedField(ResolvedField field)
        => field.Members.Count == 1
           && IdentityEncoding.IsGuidEncoded(field.RootType, field.Members[0].Name);

    private static BsonValue ScalarBson(FieldFilter f, ResolvedField field, bool guidEncoded)
        => ToBson(FilterValueConverter.Convert(ScalarRaw(f.Value), field.ComparableType), guidEncoded);

    private static IEnumerable<BsonValue> SetBson(FieldFilter f, ResolvedField field, bool guidEncoded)
        => SetRaw(f.Value).Select(x => ToBson(FilterValueConverter.Convert(x, field.ComparableType), guidEncoded));

    /// <summary>
    /// Convert a coerced comparison value to its stored BSON form. For a GUID-encoded field a
    /// Guid-parseable string (or Guid) becomes UUID BinData via the shared <see cref="MongoGuidEncoding"/>
    /// codec — exactly what the write serializer stores — so the comparand matches. Otherwise the value
    /// is emitted verbatim; a guid-shaped string in a non-identity field stays a BSON string (no sniffing).
    /// </summary>
    private static BsonValue ToBson(object? value, bool guidEncoded)
    {
        if (guidEncoded)
        {
            if (value is Guid g) return MongoGuidEncoding.ToBinData(g);
            if (value is string s && MongoGuidEncoding.IsGuidEncoded(s, out var sg)) return MongoGuidEncoding.ToBinData(sg);
        }
        return value as BsonValue ?? (value is null ? BsonNull.Value : BsonValue.Create(value));
    }

    // Emit a scalar comparison as a raw BsonDocument (rendered verbatim) instead of through the
    // typed builder's object FieldDefinition, so a BsonBinaryData value is written as a top-level
    // BinData rather than wrapped in a {_v: ...} discriminator envelope (DATA-XXXX).
    private static FilterDefinition<TEntity> Doc(string name, BsonValue value)
        => new BsonDocumentFilterDefinition<TEntity>(new BsonDocument(name, value));

    private static string StringScalar(FieldFilter f) => ScalarRaw(f.Value)?.ToString() ?? string.Empty;

    /// <summary>Collection ELEMENT value coerced to the element type. Strings stay strings (BSON string).</summary>
    private static object ElementValue(object? raw, ResolvedField field)
        => FilterValueConverter.Convert(raw, field.ComparableType) ?? (object)BsonNull.Value;

    private static IEnumerable<object> ElementSet(FieldFilter f, ResolvedField field)
        => SetRaw(f.Value).Select(r => ElementValue(r, field));


    private enum AnchorMode { None, Prefix, Suffix }

    private static BsonRegularExpression Anchored(string value, bool ignoreCase, AnchorMode mode)
    {
        var escaped = Regex.Escape(value);
        var pattern = mode switch
        {
            AnchorMode.Prefix => "^" + escaped,
            AnchorMode.Suffix => escaped + "$",
            _ => escaped,
        };
        return new BsonRegularExpression(pattern, ignoreCase ? "i" : "");
    }
}
