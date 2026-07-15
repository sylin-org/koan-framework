using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Koan.Data.Abstractions.Filtering;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
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
/// Value encoding (DATA-0098): a scalar comparison value is serialized through the FIELD'S OWN
/// registered serializer — the same one the write path uses — so the comparand always matches the
/// stored representation. That covers every type the serialization config defines without special
/// cases: GUID ids/refs (UUID BinData via the per-member codec), enums (string, via the global
/// EnumRepresentationConvention), DateTime, Decimal, etc. The translator never re-derives a value's
/// BSON form, so write↔query encoding cannot drift per type. Collection (List&lt;string&gt;) ELEMENT
/// values are emitted as their native CLR type (the array element serializer is carved out — see
/// MongoDriverConfiguration).
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
    public static FilterSupport Capabilities { get; } = new(
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
        // Bind caller casing once at the shared resolver; storage translation always consumes the
        // canonical CLR member path so every adapter applies the same field semantics.
        var name = MapPath(field.CanonicalPath ?? f.Field);
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
        // The comparand is encoded through the FIELD'S OWN serializer (ResolveScalarSerializer) — the
        // same one the write path uses — so it matches the stored representation for every configured
        // type (GUID BinData, enum-as-string, DateTime, ...) without the translator re-deriving it.
        var serializer = ResolveScalarSerializer(field);
        return op switch
        {
            FilterOperator.Eq => Doc(name, ScalarBson(f, field, serializer)),
            FilterOperator.Ne => Doc(name, new BsonDocument("$ne", ScalarBson(f, field, serializer))),
            FilterOperator.Gt => Doc(name, new BsonDocument("$gt", ScalarBson(f, field, serializer))),
            FilterOperator.Gte => Doc(name, new BsonDocument("$gte", ScalarBson(f, field, serializer))),
            FilterOperator.Lt => Doc(name, new BsonDocument("$lt", ScalarBson(f, field, serializer))),
            FilterOperator.Lte => Doc(name, new BsonDocument("$lte", ScalarBson(f, field, serializer))),
            FilterOperator.In => Doc(name, new BsonDocument("$in", new BsonArray(SetBson(f, field, serializer)))),
            // Nin must match missing/null too (locked semantics).
            FilterOperator.Nin => b.Or(Doc(name, new BsonDocument("$nin", new BsonArray(SetBson(f, field, serializer)))), b.Exists(name, false), Doc(name, BsonNull.Value)),
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
            // Emit $all as a raw BsonDocument with element values encoded directly. The typed builder's
            // IEnumerable<object> FieldDefinition would serialize each element through the item serializer
            // for `object` — which is the registered JObjectSerializer (it claims typeof(object)) — and
            // that casts every string element to JObject and throws (DATA-0098). This mirrors the scalar
            // $in/$nin raw-BSON path so element encoding stays consistent and serializer-independent.
            FilterOperator.HasAll => Doc(name, new BsonDocument("$all", new BsonArray(ElementSet(f, field).Select(ToBson)))),
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

    /// <summary>
    /// The serializer registered for a top-level scalar field — the SAME one the write path uses, so it
    /// encodes the comparand exactly as stored (the per-member GUID codec, enum-as-string, DateTime, …).
    /// Returns null for nested paths or when no member map exists, in which case the value is emitted
    /// verbatim. This is what makes the translator honour the whole serialization config instead of
    /// re-deriving each type (DATA-0098).
    /// </summary>
    // Memoized (RootType, memberName) -> serializer. The class-map graph is frozen after bootstrap, so the
    // resolution is stable; the hot path (per scalar comparison during query translation) becomes a single
    // O(1) lookup with no allocation, instead of re-scanning AllMemberMaps + a LINQ closure each call. Only
    // successful (non-null) resolutions are cached, so a query that somehow ran before registration retries.
    private static readonly ConcurrentDictionary<(Type Root, string Member), IBsonSerializer> _scalarSerializers = new();

    private static IBsonSerializer? ResolveScalarSerializer(ResolvedField field)
    {
        if (field.Members.Count != 1) return null;
        (Type Root, string Member) key = (field.RootType, field.Members[0].Name);
        if (_scalarSerializers.TryGetValue(key, out var cached)) return cached;

        IBsonSerializer? resolved;
        try
        {
            // AllMemberMaps, NOT GetMemberMap: GetMemberMap returns only members DECLARED on the looked-up
            // class. A member declared on an abstract/generic base — e.g. Job<T>.LeasedUntil on the CRTP
            // base (Job<T> : Entity<T>) — is therefore absent, ResolveScalarSerializer returns null, and the
            // comparand falls through to BsonValue.Create, which throws on DateTimeOffset/TimeSpan/DateOnly/
            // TimeOnly and silently mis-encodes a base-declared enum (drift to its int ordinal). AllMemberMaps
            // includes INHERITED members with their resolved serializers (DATA-0100 residual fix).
            resolved = BsonClassMap.LookupClassMap(key.Root)
                ?.AllMemberMaps.FirstOrDefault(m => string.Equals(m.MemberName, key.Member, StringComparison.Ordinal))
                ?.GetSerializer();
        }
        catch
        {
            resolved = null;
        }

        if (resolved is not null) _scalarSerializers[key] = resolved;
        return resolved;
    }

    private static BsonValue ScalarBson(FieldFilter f, ResolvedField field, IBsonSerializer? serializer)
        => Encode(FilterValueConverter.Convert(ScalarRaw(f.Value), field.ComparableType), serializer);

    private static IEnumerable<BsonValue> SetBson(FieldFilter f, ResolvedField field, IBsonSerializer? serializer)
        => SetRaw(f.Value).Select(x => Encode(FilterValueConverter.Convert(x, field.ComparableType), serializer));

    /// <summary>
    /// Encode a comparison value to BSON through the field's own <paramref name="serializer"/>, so the
    /// comparand matches the stored form for every configured type. Falls back to a verbatim value when
    /// no serializer applies or the value's CLR type doesn't match the serializer's value type.
    ///
    /// <para>Nullable handling: when the field is e.g. <c>DateTimeOffset?</c> and the filter value is a
    /// plain <c>DateTimeOffset</c>, <see cref="Type.IsInstanceOfType"/> returns false because
    /// <c>typeof(DateTimeOffset?).IsAssignableFrom(typeof(DateTimeOffset))</c> is false (nullable
    /// assignment rules). Unwrap the nullable for the check so the field's own serializer handles the
    /// value instead of falling through to <see cref="BsonValue.Create"/> — which throws on
    /// <c>DateTimeOffset</c> because <see cref="BsonTypeMapper"/> has no built-in mapping for it.</para>
    /// </summary>
    private static BsonValue Encode(object? value, IBsonSerializer? serializer)
    {
        if (value is null) return BsonNull.Value;
        if (serializer is not null)
        {
            var serializerValueType = Nullable.GetUnderlyingType(serializer.ValueType) ?? serializer.ValueType;
            if (serializerValueType.IsInstanceOfType(value))
            {
                return SerializeToBsonValue(serializer, value);
            }
        }
        return value as BsonValue ?? BsonValue.Create(value);
    }

    private static BsonValue SerializeToBsonValue(IBsonSerializer serializer, object value)
    {
        var document = new BsonDocument();
        using (var writer = new BsonDocumentWriter(document))
        {
            writer.WriteStartDocument();
            writer.WriteName("v");
            serializer.Serialize(BsonSerializationContext.CreateRoot(writer), new BsonSerializationArgs { NominalType = serializer.ValueType }, value);
            writer.WriteEndDocument();
        }
        return document["v"];
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

    /// <summary>Coerce a collection-element value to a BSON value verbatim (no FieldDefinition serializer).</summary>
    private static BsonValue ToBson(object e) => e as BsonValue ?? BsonValue.Create(e);


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
