using System.Collections;
using System.Text.Json;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>Translates provider payload values into Koan's neutral Vector metadata algebra.</summary>
public static class VectorMetadata
{
    public static DataObject? Clone(DataObject? value) => value is null
        ? null
        : (DataObject)Copy(value)!;

    public static DataObject? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement) as DataObject
            ?? throw new InvalidOperationException("Vector provider metadata JSON must have an object root.");
    }

    public static DataObject? FromDictionary(IDictionary? values)
    {
        if (values is null) return null;
        var properties = new List<DataProperty>(values.Count);
        foreach (DictionaryEntry entry in values)
        {
            if (entry.Key is not string name)
                throw new InvalidOperationException("Vector provider metadata keys must be strings.");
            properties.Add(new DataProperty(name, Normalize(entry.Value)));
        }
        return new DataObject(properties);
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        DataObject or DataArray => value,
        JsonElement element => Read(element),
        IDictionary dictionary => FromDictionary(dictionary),
        string or bool or sbyte or byte or short or ushort or int or uint or long or ulong or
            float or double or decimal or Guid or DateOnly or TimeOnly or DateTime or DateTimeOffset or
            TimeSpan or byte[] => value,
        Enum enumeration => Convert.ChangeType(
            enumeration,
            Enum.GetUnderlyingType(enumeration.GetType()),
            System.Globalization.CultureInfo.InvariantCulture),
        IEnumerable sequence => new DataArray(sequence.Cast<object?>().Select(Normalize)),
        _ => throw new InvalidOperationException(
            $"Vector provider metadata value '{value.GetType().FullName}' is outside the neutral value algebra.")
    };

    private static object? Copy(object? value) => value switch
    {
        DataObject data => new DataObject(data.Properties.Select(property =>
            new DataProperty(property.Name, Copy(property.Value)))),
        DataArray array => new DataArray(array.Items.Select(Copy)),
        byte[] bytes => bytes.ToArray(),
        _ => value
    };

    private static object? Read(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new DataObject(element.EnumerateObject().Select(property =>
            new DataProperty(property.Name, Read(property.Value)))),
        JsonValueKind.Array => new DataArray(element.EnumerateArray().Select(Read)),
        JsonValueKind.String => element.TryGetDateTimeOffset(out var date) ? date : element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException($"Vector provider metadata contains unsupported JSON kind '{element.ValueKind}'.")
    };
}
