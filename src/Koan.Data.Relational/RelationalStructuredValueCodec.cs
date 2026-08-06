using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Relational;

/// <summary>One relational-family conversion between Data's structured value algebra and JSON storage.</summary>
public sealed class RelationalStructuredValueCodec
{
    private readonly JsonSerializer _serializer;

    public RelationalStructuredValueCodec(IEnumerable<DataSegmentationField>? segmentationFields = null)
    {
        var settings = ComparableScalarEncoding.Apply(new JsonSerializerSettings(), segmentationFields);
        _serializer = JsonSerializer.Create(settings);
    }

    public string Serialize(object? value, bool includeManagedFields = false)
    {
        var token = ToToken(value);
        if (includeManagedFields && token is JObject item && ManagedFieldWriteScope.Effective is { } managed)
        {
            foreach (var pair in managed)
            {
                EntityFamilyStorage.EnsureFieldAvailable(pair.Key, "A framework-managed field");
                item[pair.Key] = ToToken(pair.Value);
            }
        }
        return token.ToString(Formatting.None);
    }

    public object? Deserialize(object? value)
    {
        if (value is null or DBNull) return null;
        var json = value as string ?? value.ToString()
            ?? throw new InvalidDataException("The relational provider returned an empty structured value.");
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        return FromToken(JToken.ReadFrom(reader));
    }

    public static object? ReadPath(object? root, IReadOnlyList<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            if (current is not DataObject item) return null;
            var matches = item.Properties.Where(property =>
                string.Equals(property.Name, segment, StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException($"Structured value has duplicate property '{segment}'.");
            if (matches.Length == 0) return null;
            current = matches[0].Value;
        }
        return current;
    }

    private JToken ToToken(object? value) => value switch
    {
        null => JValue.CreateNull(),
        DataObject item => ToObject(item),
        DataArray array => new JArray(array.Items.Select(ToToken)),
        byte[] bytes => new JValue(Convert.ToBase64String(bytes)),
        _ => JToken.FromObject(value, _serializer)
    };

    private JObject ToObject(DataObject value)
    {
        var result = new JObject();
        foreach (var property in value.Properties)
        {
            if (result.ContainsKey(property.Name))
                throw new InvalidDataException($"JSON storage cannot preserve duplicate property '{property.Name}'.");
            result[property.Name] = ToToken(property.Value);
        }
        return result;
    }

    private static object? FromToken(JToken token) => token.Type switch
    {
        JTokenType.Null or JTokenType.Undefined => null,
        JTokenType.Boolean => token.Value<bool>(),
        JTokenType.Integer => token.Value<long>(),
        JTokenType.Float => token.Value<decimal>(),
        JTokenType.String => token.Value<string>(),
        JTokenType.Bytes => token.Value<byte[]>(),
        JTokenType.Array => new DataArray(token.Children().Select(FromToken)),
        JTokenType.Object => new DataObject(((JObject)token).Properties()
            .Select(property => new DataProperty(property.Name, FromToken(property.Value)))),
        _ => token.ToString(Formatting.None)
    };
}
