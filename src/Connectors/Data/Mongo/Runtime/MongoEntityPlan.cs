using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using System.Reflection;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoEntityPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    // The strategy the serializer names document fields with. Query, sort and order paths resolve each segment through
    // it, so a filter addresses the field the document actually holds: Newtonsoft lower-cases a whole leading capital
    // run, so ISBN is stored as "isbn" and IPAddress as "ipAddress", not "iSBN" and "iPAddress" (NK-029).
    private static readonly CamelCaseNamingStrategy Naming = new();
    private readonly MappingPlan? _mapping;
    private readonly JsonSerializerSettings _json;
    private readonly string _identityJsonName;

    public MongoEntityPlan(IServiceProvider services, string source, MappingPlan? mapping)
    {
        _mapping = mapping;
        Optimization = services.GetStorageOptimization<TEntity, TKey>();
        IdentityName = Optimization.IdPropertyName;
        var segmentation = services.GetRequiredService<DataSegmentationPlan>().For(typeof(TEntity));
        if (mapping is not null &&
            (!segmentation.IsEmpty || ManagedFieldRegistry.ForType(typeof(TEntity)).Count != 0))
            throw new MappingCompilationException(source, typeof(TEntity),
                "Explicit MongoDB maps cannot preserve framework-managed row fields. Use a managed map or a separate source/container axis.");

        ScopeFields = segmentation.Fields.Select(static field => field.StorageName).ToArray();
        _identityJsonName = Naming.GetPropertyName(IdentityName, hasSpecifiedName: false);
        _json = EntityJsonSerialization.Apply(new JsonSerializerSettings
        {
            ContractResolver = new ManagedFieldJsonInjector(segmentation.Fields) { NamingStrategy = Naming },
            DateParseHandling = DateParseHandling.None,
            NullValueHandling = NullValueHandling.Include,
            Converters =
            {
                new DateTimeOffsetConverter(),
                new TimeSpanConverter(),
                new DateOnlyConverter(),
                new TimeOnlyConverter()
            }
        });
    }

    public StorageOptimizationInfo Optimization { get; }

    /// <summary>
    /// The managed fields that scope this entity's rows (the tenant), in order, as stored in each document. A unique
    /// index leads with them (DATA-0150). Empty when the host has no row-scoped segmentation for the entity.
    /// </summary>
    public IReadOnlyList<string> ScopeFields { get; }
    public string IdentityName { get; }
    public bool IsMapped => _mapping is not null;
    public MappingPlan? Mapping => _mapping;
    public StorageAddress? MappedContainer => _mapping?.Container;

    public BsonDocument Write(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_mapping is not null)
        {
            var mappedDocument = new BsonDocument();
            foreach (var value in _mapping.Write(entity).Values)
                MongoValues.Set(mappedDocument, value.Path, MongoValues.FromNeutral(value.Value));
            return mappedDocument;
        }

        // Retain CLR numeric widths, decimal precision and binary/date tokens through BSON conversion.
        // A JSON text round-trip erases those distinctions before MongoValues can preserve them.
        using var writer = new JTokenWriter();
        JsonSerializer.Create(_json).Serialize(writer, entity, entity.GetType());
        var payload = writer.Token as JObject
            ?? throw new InvalidDataException($"MongoDB serialization of '{typeof(TEntity).FullName}' did not produce an object.");
        var identity = payload.Property(_identityJsonName, StringComparison.OrdinalIgnoreCase)
            ?? throw new InvalidDataException(
                $"MongoDB could not locate identity '{IdentityName}' while serializing '{typeof(TEntity).FullName}'.");
        var document = MongoValues.FromJson(payload).AsBsonDocument;
        document.Remove(identity.Name);
        document[Infrastructure.Constants.Storage.Identity] = MongoValues.FromJson(identity.Value);
        return document;
    }

    public UpdateDefinition<BsonDocument> Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_mapping is null)
            throw new InvalidOperationException("Managed MongoDB documents use replacement writes.");
        var updates = _mapping.Write(entity).Values.Select(value =>
        {
            var path = MongoValues.Path(value.Path);
            var encoded = MongoValues.FromNeutral(value.Value);
            return string.Equals(path, Infrastructure.Constants.Storage.Identity, StringComparison.Ordinal)
                ? Builders<BsonDocument>.Update.SetOnInsert(path, encoded)
                : Builders<BsonDocument>.Update.Set(path, encoded);
        });
        return Builders<BsonDocument>.Update.Combine(updates);
    }

    public TEntity Read(BsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_mapping is not null)
        {
            var values = new List<MappedValue>(_mapping.Bindings.Count);
            foreach (var binding in _mapping.Bindings)
                if (MongoValues.TryGet(document, binding.PhysicalPath, out var value))
                    values.Add(new MappedValue(
                        binding.Id,
                        binding.PhysicalPath,
                        binding.Shape,
                        MongoValues.ToNeutral(value)));
            return _mapping.Hydrate<TEntity>(values);
        }

        var payload = (JObject)MongoValues.ToJson(document);
        if (payload.TryGetValue(Infrastructure.Constants.Storage.Identity, out var identity))
        {
            payload.Remove(Infrastructure.Constants.Storage.Identity);
            payload[_identityJsonName] = identity;
        }
        return (TEntity)(payload.ToObject(typeof(TEntity), JsonSerializer.Create(_json))
            ?? throw new InvalidDataException($"MongoDB returned an empty document for '{typeof(TEntity).FullName}'."));
    }

    public FilterDefinition<BsonDocument> Identity(TKey id)
    {
        if (_mapping is null)
            return Builders<BsonDocument>.Filter.Eq(
                Infrastructure.Constants.Storage.Identity,
                MongoValues.FromNeutral(id));
        return Identity(_mapping.WriteIdentity(id));
    }

    public FilterDefinition<BsonDocument> Identity(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_mapping is null) return Identity(entity.Id);
        var values = _mapping.Identity.Parts.Select(part =>
        {
            var binding = _mapping.Bindings.Single(candidate => candidate.Id == part.Id);
            return new MappedValue(binding.Id, binding.PhysicalPath, binding.Shape, binding.Encode(binding.Read(entity)));
        });
        return Identity(new MappedRecord(values, new MappingReceipt(
            _mapping.Id,
            MappingConsumer.Filter,
            _mapping.Identity.Parts.Select(static part => part.Id))));
    }

    public FilterDefinition<BsonDocument> WriteGuard()
    {
        var guard = ManagedFieldWriteScope.Current;
        if (guard is null || guard.Count == 0) return FilterDefinition<BsonDocument>.Empty;
        var filters = guard.Select(value => Builders<BsonDocument>.Filter.Eq(
            value.Key,
            MongoValues.FromNeutral(value.Value)));
        return Builders<BsonDocument>.Filter.And(filters);
    }

    public string Field(FieldPath path, ResolvedField resolved, MappingConsumer consumer)
    {
        if (resolved.IsManaged) return resolved.StorageName!;
        if (_mapping is not null)
        {
            var use = _mapping.Use(MappingPath.Of(path.Segments.ToArray()), consumer);
            return MongoValues.Path(use.Bindings.Single().PhysicalPath);
        }
        if (path.Segments.Count == 1 && string.Equals(path.Leaf, IdentityName, StringComparison.Ordinal))
            return Infrastructure.Constants.Storage.Identity;
        return string.Join('.', path.Segments.Select(Stored));
    }

    /// <summary>
    /// The index key spelling Koan used before index keys followed the serializer (lower-casing only the first letter),
    /// or <see langword="null"/> when it equals the stored spelling or this plan is mapped. Used only to recognize
    /// indexes an older version created, so a corrective message can name them (DATA-0145).
    /// </summary>
    public string? LegacyIndexField(FieldPath path)
    {
        if (_mapping is not null) return null;
        if (path.Segments.Count == 1 && string.Equals(path.Leaf, IdentityName, StringComparison.Ordinal)) return null;
        var legacy = string.Join('.', path.Segments.Select(LegacyIndexName));
        return string.Equals(legacy, string.Join('.', path.Segments.Select(Stored)), StringComparison.Ordinal) ? null : legacy;
    }

    /// <summary>
    /// The aggregation expression an order key reaches for when its path runs through a collection.
    ///
    /// <para><c>-Sightings.LastChangedAt</c> orders widgets by their latest sighting, which is an aggregate
    /// over a nested array rather than a field. <c>find</c> cannot sort by one, but the aggregation language
    /// states it plainly — <c>$max</c> over a <c>$map</c> of the array — so MongoDB orders and pages the query
    /// itself instead of returning the whole collection for the framework to sort.</para>
    ///
    /// <para>An absent or empty array maps to nothing and aggregates to null, and BSON sorts null before every
    /// value, so a widget with no sightings lands first ascending and last descending — where the framework's
    /// own sorter puts it.</para>
    ///
    /// <para>Returns <see langword="null"/> where this plan cannot express the key: an explicit map may bind
    /// one logical path across several physical ones, a second collection inside the path would need an
    /// aggregate of aggregates, and positional First/Last depend on an element order a document store does not
    /// promise.</para>
    /// </summary>
    public BsonDocument? CollectionOrderExpression(MemberPath path, SortAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(path);
        var accumulator = aggregation switch
        {
            // None over a collection leaf means what it means to the in-memory sorter: take the maximum.
            SortAggregation.Max or SortAggregation.None => "$max",
            SortAggregation.Min => "$min",
            _ => null
        };
        if (accumulator is null || _mapping is not null) return null;

        var boundary = path.CollectionSegmentIndex;
        if (boundary <= 0 || boundary >= path.Members.Count) return null;
        for (var index = boundary; index < path.Members.Count; index++)
            if (IsCollectionMember(path.Members[index])) return null;

        var array = string.Join('.', path.Members.Take(boundary).Select(static member => Stored(member.Name)));
        var leaf = string.Join('.', path.Members.Skip(boundary).Select(static member => Stored(member.Name)));
        return new BsonDocument(accumulator, new BsonDocument("$map", new BsonDocument
        {
            ["input"] = new BsonDocument("$ifNull", new BsonArray { "$" + array, new BsonArray() }),
            ["as"] = "koanElement",
            ["in"] = (Nullable.GetUnderlyingType(path.ValueType) ?? path.ValueType).IsEnum
                ? EnumOrderExpression("$$koanElement." + leaf, path.ValueType)
                : new BsonString("$$koanElement." + leaf)
        }));
    }

    private static bool IsCollectionMember(MemberInfo member)
    {
        var type = member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => typeof(object)
        };
        return type != typeof(string) && type != typeof(byte[]) &&
               typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    public BsonValue FilterValue(FieldPath path, ResolvedField resolved, object? value)
    {
        var converted = FilterValueConverter.Convert(value, resolved.ComparableType);
        if (_mapping is not null)
        {
            var binding = _mapping.Use(MappingPath.Of(path.Segments.ToArray()), MappingConsumer.Filter)
                .Bindings.Single();
            if (!resolved.TargetsCollection) converted = binding.Encode(converted);
        }
        return MongoValues.FromNeutral(converted);
    }

    public bool UsesEnumNames(FieldPath path, ResolvedField resolved, MappingConsumer consumer = MappingConsumer.Filter)
        => (Nullable.GetUnderlyingType(resolved.ComparableType) ?? resolved.ComparableType).IsEnum
            && (_mapping is null || _mapping.Use(MappingPath.Of(path.Segments.ToArray()), consumer)
                .Bindings.Single().Descriptor.Codec is null);

    public static BsonDocument EnumOrderExpression(string value, Type enumType)
        => new("$switch", new BsonDocument
        {
            ["branches"] = new BsonArray(EnumStorageEncoding.OrderedValues(enumType).Select(pair => new BsonDocument
            {
                ["case"] = new BsonDocument("$eq", new BsonArray { value, new BsonDocument("$literal", pair.Key) }),
                ["then"] = new BsonDecimal128(pair.Value)
            })),
            ["default"] = BsonNull.Value
        });

    private static FilterDefinition<BsonDocument> Identity(MappedRecord record)
    {
        var filters = record.Values.Select(value => Builders<BsonDocument>.Filter.Eq(
            MongoValues.Path(value.Path),
            MongoValues.FromNeutral(value.Value)));
        return Builders<BsonDocument>.Filter.And(filters);
    }

    /// <summary>The field name the serializer writes for a CLR member name.</summary>
    private static string Stored(string member) => Naming.GetPropertyName(member, hasSpecifiedName: false);

    /// <summary>The first-letter-only spelling index keys had before DATA-0145; kept to recognize them.</summary>
    private static string LegacyIndexName(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];

    private abstract class StructConverter<T> : JsonConverter where T : struct
    {
        public override bool CanConvert(Type objectType) =>
            (Nullable.GetUnderlyingType(objectType) ?? objectType) == typeof(T);

        public override void WriteJson(JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (value is null) { writer.WriteNull(); return; }
            Write(writer, (T)value);
        }

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            Newtonsoft.Json.JsonSerializer serializer) =>
            reader.TokenType == JsonToken.Null ? null : Read(reader.Value);

        protected abstract void Write(JsonWriter writer, T value);
        protected abstract T Read(object? value);
    }

    private sealed class DateTimeOffsetConverter : StructConverter<DateTimeOffset>
    {
        protected override void Write(JsonWriter writer, DateTimeOffset value) =>
            writer.WriteValue(value.UtcDateTime);

        protected override DateTimeOffset Read(object? value) => value switch
        {
            DateTime dateTime => new DateTimeOffset(
                dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime()),
            _ => DateTimeOffset.Parse(value!.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .ToUniversalTime()
        };
    }

    private sealed class TimeSpanConverter : StructConverter<TimeSpan>
    {
        protected override void Write(JsonWriter writer, TimeSpan value) => writer.WriteValue(value.Ticks);
        protected override TimeSpan Read(object? value) =>
            TimeSpan.FromTicks(Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    private sealed class DateOnlyConverter : StructConverter<DateOnly>
    {
        protected override void Write(JsonWriter writer, DateOnly value) =>
            writer.WriteValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        protected override DateOnly Read(object? value) =>
            DateOnly.ParseExact(value!.ToString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private sealed class TimeOnlyConverter : StructConverter<TimeOnly>
    {
        protected override void Write(JsonWriter writer, TimeOnly value) =>
            writer.WriteValue(value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        protected override TimeOnly Read(object? value) =>
            TimeOnly.ParseExact(value!.ToString()!, "HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    }
}
