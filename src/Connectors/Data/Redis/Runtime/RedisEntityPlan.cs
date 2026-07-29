using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Redis.Runtime;

internal sealed class RedisEntityPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MappingPlan? _mapping;
    private readonly IReadOnlyList<DataSegmentationField> _segmentationFields;

    internal RedisEntityPlan(IServiceProvider services, string source, MappingPlan? mapping)
    {
        _mapping = mapping;
        if (mapping?.Identity.IsGenerated == true)
            throw new MappingCompilationException(source, typeof(TEntity),
                "Redis keys are application-assigned. Remove Generated() or assign the complete entity key before Save().");
        var segmentation = services.GetRequiredService<DataSegmentationPlan>().For(typeof(TEntity));
        _segmentationFields = segmentation.Fields;
        if (mapping is not null && (!segmentation.IsEmpty || ManagedFieldRegistry.ForType(typeof(TEntity)).Count != 0))
            throw new MappingCompilationException(source, typeof(TEntity),
                "An explicit Redis map cannot preserve framework-managed record fields. Use a separate managed container axis.");
        if (mapping?.Container.Namespace.Count > 0)
            throw new MappingCompilationException(source, typeof(TEntity),
                "Redis containers have one name and do not accept Namespace segments.");
    }

    internal MappingPlan? Mapping => _mapping;
    internal string? MappedContainer => _mapping?.Container.Name;

    internal JObject Create(TEntity entity)
    {
        if (_mapping is null)
        {
            var managedDocument = JObject.Parse(EntityJsonSerialization.SerializeDocument(entity));
            ManagedFieldJsonInjector.InjectManaged(managedDocument, ManagedFieldWriteScope.Effective);
            return managedDocument;
        }
        var document = new JObject();
        Apply(document, entity, MappingWriteOperation.Insert);
        return document;
    }

    internal void Apply(JObject document, TEntity entity, MappingWriteOperation operation)
    {
        if (_mapping is null) throw new InvalidOperationException("Managed Redis entities use whole-document replacement.");
        foreach (var value in _mapping.Write(entity, operation).Values)
            Set(document, value.Path, Json(value.Value));
    }

    internal TEntity Read(string json)
        => ReadRecord(json).Entity;

    internal RedisRecord<TEntity> ReadRecord(string json)
    {
        if (_mapping is null)
        {
            var managedDocument = JObject.Parse(json);
            var managed = ManagedFieldJsonInjector.ExtractManaged(managedDocument, typeof(TEntity), _segmentationFields);
            return new RedisRecord<TEntity>(
                (TEntity)EntityJsonSerialization.Materialize(
                    managedDocument,
                    typeof(TEntity),
                    JsonSerializer.Create(EntityJsonSerialization.Apply(new JsonSerializerSettings()))),
                managed);
        }
        var document = JObject.Parse(json);
        var values = new List<MappedValue>(_mapping.Bindings.Count);
        foreach (var binding in _mapping.Bindings)
            if (TryGet(document, binding.PhysicalPath, out var token))
                values.Add(new MappedValue(binding.Id, binding.PhysicalPath, binding.Shape, RedisJson.Neutral(token)));
        return new RedisRecord<TEntity>(_mapping.Hydrate<TEntity>(values), null);
    }

    internal string Identity(TKey id)
    {
        if (_mapping?.Identity.IsComposite == true)
        {
            var values = _mapping.WriteIdentity(id).Values
                .Select(static value => new JProperty(value.Path.ToString(), Json(value.Value)));
            return new JObject(values).ToString(Formatting.None);
        }
        return id switch
        {
            string value => value,
            Guid value => value.ToString("D"),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => JsonConvert.SerializeObject(id, Formatting.None)
        };
    }

    private static void Set(JObject root, PhysicalPath path, JToken value)
    {
        var names = new[] { path.Name }.Concat(path.Segments).ToArray();
        var current = root;
        for (var index = 0; index < names.Length - 1; index++)
        {
            if (current[names[index]] is not JObject child)
            {
                child = new JObject();
                current[names[index]] = child;
            }
            current = child;
        }
        current[names[^1]] = value;
    }

    private static bool TryGet(JObject root, PhysicalPath path, out JToken value)
    {
        JToken? current = root[path.Name];
        foreach (var segment in path.Segments) current = (current as JObject)?[segment];
        value = current!;
        return current is not null;
    }

    private static JToken Json(object? value) => value switch
    {
        null => JValue.CreateNull(),
        DataObject data => new JObject(data.Properties.Select(property => new JProperty(property.Name, Json(property.Value)))),
        DataArray data => new JArray(data.Items.Select(Json)),
        byte[] bytes => new JValue(Convert.ToBase64String(bytes)),
        _ => JToken.FromObject(value)
    };

}

internal static class RedisJson
{
    internal static object? Neutral(JToken value) => value.Type switch
    {
        JTokenType.Null or JTokenType.Undefined => null,
        JTokenType.Object => new DataObject(((JObject)value).Properties()
            .Select(property => new DataProperty(property.Name, Neutral(property.Value)))),
        JTokenType.Array => new DataArray(((JArray)value).Select(Neutral)),
        _ when value is JValue scalar => scalar.Value switch
        {
            Uri uri => uri.ToString(),
            char character => character.ToString(),
            _ => scalar.Value
        },
        _ => throw new InvalidDataException($"Redis returned unsupported JSON token '{value.Type}'.")
    };
}

internal sealed record RedisRecord<TEntity>(TEntity Entity, IReadOnlyDictionary<string, object?>? Managed);

internal static class RedisKeyLayout
{
    internal static string Encode(string value)
    {
        if (value.Length is > 0 and <= 256 && value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~'))
            return value;
        return "b64-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static string Prefix(string source, int database, string container)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source + "\n" + database + "\n" + container));
        var tag = Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
        return "koan:{" + tag + "}";
    }
}
