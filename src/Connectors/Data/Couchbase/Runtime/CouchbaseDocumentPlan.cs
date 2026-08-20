using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal sealed class CouchbaseDocumentPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MappingPlan? _mapping;
    private readonly string _identityName;

    internal CouchbaseDocumentPlan(IServiceProvider services, string source, MappingPlan? mapping)
    {
        _mapping = mapping;
        _identityName = services.GetStorageOptimization<TEntity, TKey>().IdPropertyName;
        var segmentation = services.GetRequiredService<DataSegmentationPlan>().For(typeof(TEntity));
        if (mapping?.Identity.IsGenerated == true)
            throw new MappingCompilationException(source, typeof(TEntity),
                "Couchbase document keys are application-assigned. Remove Generated() or assign the entity key before Save().");
        if (mapping is not null &&
            (!segmentation.IsEmpty || ManagedFieldRegistry.ForType(typeof(TEntity)).Count != 0))
            throw new MappingCompilationException(source, typeof(TEntity),
                "An explicit Couchbase map cannot preserve framework-managed record fields. Use managed storage or a separate source/container axis.");
    }

    internal MappingPlan? Mapping => _mapping;
    internal StorageAddress? MappedContainer => _mapping?.Container;

    internal JObject Write(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_mapping is null)
        {
            var document = JObject.Parse(EntityJsonSerialization.SerializeDocument(entity));
            ManagedFieldJsonInjector.InjectManaged(document, ManagedFieldWriteScope.Effective);
            return document;
        }

        var mapped = new JObject();
        foreach (var value in _mapping.Write(entity).Values)
            Set(mapped, value.Path, ToJson(value.Value));
        return mapped;
    }

    internal void ApplyMappedWrite(JObject target, TEntity entity)
    {
        if (_mapping is null) throw new InvalidOperationException("Managed Couchbase documents use replacement writes.");
        foreach (var value in _mapping.Write(entity, MappingWriteOperation.Update).Values)
            Set(target, value.Path, ToJson(value.Value));
    }

    internal TEntity Read(JObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_mapping is null)
            return (TEntity)EntityJsonSerialization.DeserializeDocument(
                document.ToString(Formatting.None),
                typeof(TEntity));

        var values = new List<MappedValue>(_mapping.Bindings.Count);
        foreach (var binding in _mapping.Bindings)
            if (TryGet(document, binding.PhysicalPath, out var token))
                values.Add(new MappedValue(
                    binding.Id,
                    binding.PhysicalPath,
                    binding.Shape,
                    ToNeutral(token)));
        return _mapping.Hydrate<TEntity>(values);
    }

    internal string Key(TKey id)
    {
        var text = id switch
        {
            string value => value,
            Guid value => value.ToString("D"),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonConvert.SerializeObject(id, Formatting.None)
        };
        if (!string.IsNullOrEmpty(text) && Encoding.UTF8.GetByteCount(text) <= Infrastructure.Constants.MaximumKeyBytes)
            return text;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? "null"));
        return "koan:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal string Field(FieldPath path, ResolvedField resolved, MappingConsumer consumer)
    {
        if (resolved.IsManaged) return QuotePath([resolved.StorageName!]);
        var logical = resolved.CanonicalPath ?? path;
        if (_mapping is not null)
        {
            var use = _mapping.Use(MappingPath.Of(logical.Segments.ToArray()), consumer);
            var binding = use.Bindings.Count == 1
                ? use.Bindings[0]
                : throw new NotSupportedException(
                    $"Couchbase cannot use multi-binding logical path '{path}' as one SQL++ value.");
            return QuotePath([binding.PhysicalPath.Name, .. binding.PhysicalPath.Segments]);
        }
        if (logical.Segments.Count == 1 && string.Equals(logical.Leaf, _identityName, StringComparison.Ordinal))
            return QuotePath([Infrastructure.Constants.Storage.Identity]);
        return QuotePath(logical.Segments.Select(Camel));
    }

    /// <summary>
    /// The SQL++ value an order key reaches for when its path runs through a collection.
    ///
    /// <para><c>-Sightings.LastChangedAt</c> means "by the latest sighting of each widget", which is an
    /// aggregate over the nested array rather than a field. SQL++ says that directly —
    /// <c>ARRAY_MAX(ARRAY element.`lastChangedAt` FOR element IN doc.`sightings` END)</c> — so Couchbase
    /// orders and pages the query itself instead of handing the whole collection back to be sorted in
    /// memory. Ascending takes the minimum, which is the same convention the framework's own sorter uses:
    /// order by the earliest when ascending, the latest when descending.</para>
    ///
    /// <para>An empty array yields NULL, and SQL++ sorts NULL before every value, matching what the
    /// in-memory sorter does with a widget that has no sightings at all.</para>
    ///
    /// <para>Returns <see langword="null"/> when this plan cannot express the key, so the caller can decline
    /// it and let the framework finish the ordering: an explicit map may bind one logical path to several
    /// physical ones, a second collection inside the path needs an aggregate of aggregates, and positional
    /// First/Last depend on an element order a document store does not promise.</para>
    /// </summary>
    internal string? CollectionOrderValue(MemberPath path, SortAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(path);
        var function = aggregation switch
        {
            // None reaching a collection leaf means the same thing it means to the in-memory sorter: take the
            // maximum, which is direction-agnostic and never invents an element order.
            SortAggregation.Max or SortAggregation.None => "ARRAY_MAX",
            SortAggregation.Min => "ARRAY_MIN",
            _ => null
        };
        if (function is null || _mapping is not null) return null;

        var boundary = path.CollectionSegmentIndex;
        if (boundary <= 0 || boundary >= path.Members.Count) return null;
        for (var index = boundary; index < path.Members.Count; index++)
            if (IsCollection(MemberValueType(path.Members[index]))) return null;

        var collection = QuotePath(path.Members.Take(boundary).Select(static member => Camel(member.Name)));
        var leaf = QuotePath(path.Members.Skip(boundary).Select(static member => Camel(member.Name)));
        return $"{function}(ARRAY {OrderElement}.{leaf} FOR {OrderElement} IN doc.{collection} END)";
    }

    /// <summary>Binding name for the array comprehension; prefixed so it cannot collide with a document field.</summary>
    private const string OrderElement = "koan_order_element";

    private static Type MemberValueType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => typeof(object)
    };

    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    internal object? FilterValue(FieldPath path, ResolvedField resolved, object? value)
    {
        var converted = FilterValueConverter.Convert(value, resolved.ComparableType);
        if (converted is Enum enumeration)
            converted = Convert.ChangeType(enumeration, Enum.GetUnderlyingType(enumeration.GetType()), CultureInfo.InvariantCulture);
        if (_mapping is not null)
        {
            var logical = resolved.CanonicalPath ?? path;
            var binding = _mapping.Use(MappingPath.Of(logical.Segments.ToArray()), MappingConsumer.Filter)
                .Bindings.Single();
            if (!resolved.TargetsCollection) converted = binding.Encode(converted);
        }
        return converted;
    }

    internal static string QuotePath(IEnumerable<string> segments) =>
        string.Join('.', segments.Select(static segment => "`" + segment.Replace("`", "``") + "`"));

    private static string Camel(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0]) ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static void Set(JObject root, PhysicalPath path, JToken value)
    {
        var names = new[] { path.Name }.Concat(path.Segments).ToArray();
        JObject current = root;
        for (var index = 0; index < names.Length - 1; index++)
        {
            if (current[names[index]] is not JObject next)
            {
                next = new JObject();
                current[names[index]] = next;
            }
            current = next;
        }
        current[names[^1]] = value;
    }

    private static bool TryGet(JObject root, PhysicalPath path, out JToken value)
    {
        JToken? current = root[path.Name];
        foreach (var segment in path.Segments)
            current = (current as JObject)?[segment];
        value = current!;
        return current is not null;
    }

    private static JToken ToJson(object? value) => value switch
    {
        null => JValue.CreateNull(),
        DataObject data => new JObject(data.Properties.Select(property =>
            new JProperty(property.Name, ToJson(property.Value)))),
        DataArray data => new JArray(data.Items.Select(ToJson)),
        byte[] bytes => new JValue(Convert.ToBase64String(bytes)),
        _ => JToken.FromObject(value)
    };

    private static object? ToNeutral(JToken value) => CouchbaseNeutralReader.Neutral(value);
}
