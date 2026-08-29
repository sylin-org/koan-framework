using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Connector.CouchDb.Runtime;

/// <summary>
/// The entity as a CouchDB document. The default path serializes the whole entity with Newtonsoft —
/// camelCase properties, framework-managed discriminators injected as top-level fields (so a Mango
/// selector can enforce row isolation), and the temporal encodings every Koan store shares. The
/// store's reserved <c>_id</c> carries the identity; <c>_rev</c> never reaches entity state.
///
/// <para>Explicit maps are refused for now: a declared map must keep framework-managed fields
/// isolated, and CouchDB has no generated-column route to mirror a mapped subtree for row isolation.
/// The refusal names the gap rather than shipping a map that silently loses it.</para>
/// </summary>
internal sealed class CouchDbEntityPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly JsonSerializerSettings _json;
    private readonly string _identityJsonName;

    public CouchDbEntityPlan(IServiceProvider services)
    {
        Optimization = services.GetStorageOptimization<TEntity, TKey>();
        IdentityName = Optimization.IdPropertyName;
        var segmentation = services.GetRequiredService<DataSegmentationPlan>().For(typeof(TEntity));
        _hasManaged = !segmentation.IsEmpty || ManagedFieldRegistry.ForType(typeof(TEntity)).Count != 0;
        var naming = new CamelCaseNamingStrategy();
        _identityJsonName = naming.GetPropertyName(IdentityName, hasSpecifiedName: false);
        _json = EntityJsonSerialization.Apply(new JsonSerializerSettings
        {
            ContractResolver = new ManagedFieldJsonInjector(segmentation.Fields) { NamingStrategy = naming },
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

    private readonly bool _hasManaged;

    public StorageOptimizationInfo Optimization { get; }
    public string IdentityName { get; }
    public bool SupportsMappingPlans => false;

    public void DemandNoMappingPlan(MappingPlan? mapping, string source)
    {
        if (mapping is null) return;
        throw new MappingCompilationException(source, typeof(TEntity),
            "This CouchDB adapter does not honor explicit mapping plans yet: it cannot keep framework-managed " +
            "row fields isolated under a declared map. Use the default entity mapping on this store.");
    }

    /// <summary>
    /// The storage key framework-managed discriminators live under. CouchDB rejects any top-level
    /// member that starts with an underscore, and managed storage names are underscore-prefixed, so
    /// they ride in a legal <c>koan</c> subdocument on write and are hoisted back on read — the
    /// selector path (<see cref="ManagedFieldPath"/>) reads them there.
    /// </summary>
    internal const string ManagedMember = "koan";

    public static string ManagedFieldPath(string storageName) => $"{ManagedMember}.{storageName}";

    /// <summary>The document body for one entity, without the reserved identity key.</summary>
    public JObject Write(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var json = JsonConvert.SerializeObject(entity, entity.GetType(), _json);
        var payload = JObject.Parse(json);
        var identity = payload.Property(_identityJsonName, StringComparison.OrdinalIgnoreCase)
            ?? throw new InvalidDataException(
                $"CouchDB could not locate identity '{IdentityName}' while serializing '{typeof(TEntity).FullName}'.");
        var body = new JObject(payload.Properties().Where(static property =>
            !string.Equals(property.Name, "_rev", StringComparison.Ordinal)));
        body.Remove(identity.Name);
        HoistUnderscoreMembers(body);
        return body;
    }

    private static void HoistUnderscoreMembers(JObject body)
    {
        var reserved = body.Properties()
            .Where(static property => property.Name.StartsWith("_", StringComparison.Ordinal))
            .ToArray();
        if (reserved.Length == 0) return;
        var koan = body[ManagedMember] as JObject ?? new JObject();
        foreach (var property in reserved)
        {
            body.Remove(property.Name);
            koan[property.Name] = property.Value;
        }
        body[ManagedMember] = koan;
    }

    public TEntity Read(JObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var payload = (JObject)document.DeepClone();
        if (payload.TryGetValue(Infrastructure.Constants.Storage.Identity, out var identity))
        {
            payload.Remove(Infrastructure.Constants.Storage.Identity);
            payload[_identityJsonName] = identity;
        }
        payload.Remove(Infrastructure.Constants.Storage.Rev);
        if (payload[ManagedMember] is JObject koan)
        {
            payload.Remove(ManagedMember);
            foreach (var property in koan.Properties())
                payload[property.Name] = property.Value;
        }
        return JsonConvert.DeserializeObject<TEntity>(payload.ToString(Formatting.None), _json)
            ?? throw new InvalidDataException($"CouchDB returned an empty document for '{typeof(TEntity).FullName}'.");
    }

    /// <summary>The identity as the store's <c>_id</c> string, in a lossless invariant form.</summary>
    public string IdentityId(TKey id) => DocumentValueConversion.ToStringInvariant(id!);

    public string IdentityId(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return DocumentValueConversion.ToStringInvariant(entity.Id!);
    }

    public TKey ParseIdentity(string id) =>
        (TKey)DocumentValueConversion.ToClr(id, typeof(TKey));

    /// <summary>The guard a scoped write must satisfy, evaluated on the stored document.</summary>
    public bool MatchesWriteGuard(JObject stored)
    {
        var guard = ManagedFieldWriteScope.Current;
        if (guard is null || guard.Count == 0) return true;
        foreach (var pair in guard)
        {
            var actual = stored.SelectToken($"{ManagedMember}.{pair.Key}") is JValue value ? value.Value
                : stored[pair.Key] is JValue direct ? direct.Value
                : null;
            var expected = pair.Value;
            if (!JTokenEquals(actual, expected)) return false;
        }
        return true;
    }

    private static bool JTokenEquals(object? actual, object? expected)
    {
        if (actual is null && expected is null) return true;
        if (actual is null || expected is null) return false;
        var expectedToken = expected is JToken token ? token : new JValue(expected);
        return JToken.DeepEquals(new JValue(actual), expectedToken) ||
               string.Equals(actual.ToString(), expected.ToString(), StringComparison.Ordinal);
    }

    // Temporal converters shared in shape with the Mongo exemplar; the encodings are the DATA-0100 forms.
    private abstract class StructConverter<T> : JsonConverter where T : struct
    {
        public override bool CanConvert(Type objectType) =>
            (Nullable.GetUnderlyingType(objectType) ?? objectType) == typeof(T);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is null) { writer.WriteNull(); return; }
            Write(writer, (T)value);
        }

        public override object? ReadJson(
            JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
            reader.TokenType == JsonToken.Null ? null : Read(reader.Value);

        protected abstract void Write(JsonWriter writer, T value);
        protected abstract T Read(object? value);
    }

    private sealed class DateTimeOffsetConverter : StructConverter<DateTimeOffset>
    {
        protected override void Write(JsonWriter writer, DateTimeOffset value) => writer.WriteValue(value.UtcDateTime);
        protected override DateTimeOffset Read(object? value) => value switch
        {
            DateTime dateTime => new DateTimeOffset(
                dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime()),
            _ => DateTimeOffset.Parse(value!.ToString()!, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime()
        };
    }

    private sealed class TimeSpanConverter : StructConverter<TimeSpan>
    {
        protected override void Write(JsonWriter writer, TimeSpan value) => writer.WriteValue(value.Ticks);
        protected override TimeSpan Read(object? value) =>
            TimeSpan.FromTicks(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class DateOnlyConverter : StructConverter<DateOnly>
    {
        protected override void Write(JsonWriter writer, DateOnly value) =>
            writer.WriteValue(value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        protected override DateOnly Read(object? value) =>
            DateOnly.ParseExact(value!.ToString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class TimeOnlyConverter : StructConverter<TimeOnly>
    {
        protected override void Write(JsonWriter writer, TimeOnly value) =>
            writer.WriteValue(value.ToString("HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture));
        protected override TimeOnly Read(object? value) =>
            TimeOnly.ParseExact(value!.ToString()!, "HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Conversions between the store's JSON values and CLR forms, without a driver to lean on.</summary>
internal static class DocumentValueConversion
{
    public static string ToStringInvariant(object value) => value switch
    {
        string s => s,
        Guid g => g.ToString("D"),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? throw new InvalidOperationException(
            $"The identity value '{value}' has no invariant string form for CouchDB _id.")
    };

    public static object ToClr(string id, Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value == typeof(string)) return id;
        if (value == typeof(Guid)) return Guid.Parse(id);
        return Convert.ChangeType(id, value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
