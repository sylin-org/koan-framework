using System.Collections;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

/// <summary>Translates provider payload values into Koan's neutral Vector metadata algebra.</summary>
public static class VectorMetadata
{
    private const string TypeMarker = "__koan_type";
    private const string TypedValue = "value";

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

    /// <summary>Writes neutral metadata without reflection or provider-specific object contracts.</summary>
    public static string? ToJson(DataObject? value)
    {
        if (value is null) return null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) Write(writer, value);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
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

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case DataObject data:
                writer.WriteStartObject();
                foreach (var property in data.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case DataArray array:
                writer.WriteStartArray();
                foreach (var item in array.Items) Write(writer, item);
                writer.WriteEndArray();
                break;
            case string text: writer.WriteStringValue(text); break;
            case bool boolean: writer.WriteBooleanValue(boolean); break;
            case sbyte number: WriteTagged(writer, "i8", number); break;
            case byte number: WriteTagged(writer, "u8", number); break;
            case short number: WriteTagged(writer, "i16", number); break;
            case ushort number: WriteTagged(writer, "u16", number); break;
            case int number: WriteTagged(writer, "i32", number); break;
            case uint number: WriteTagged(writer, "u32", number); break;
            case long number: WriteTagged(writer, "i64", number); break;
            case ulong number: WriteTagged(writer, "u64", number); break;
            case float number: WriteTagged(writer, "f32", number); break;
            case double number: WriteTagged(writer, "f64", number); break;
            case decimal number: WriteTagged(writer, "dec", number); break;
            case Guid guid: WriteTagged(writer, "guid", guid.ToString("D")); break;
            case DateOnly date: WriteTagged(writer, "date", date.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeOnly time: WriteTagged(writer, "time", time.ToString("O", CultureInfo.InvariantCulture)); break;
            case DateTime dateTime: WriteTagged(writer, "datetime", dateTime.ToString("O", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dateTimeOffset: WriteTagged(writer, "datetimeoffset", dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeSpan duration: WriteTagged(writer, "duration", duration.ToString("c", CultureInfo.InvariantCulture)); break;
            case byte[] bytes: WriteTagged(writer, "bytes", Convert.ToBase64String(bytes)); break;
            default: throw new InvalidOperationException(
                $"Vector metadata value '{value.GetType().FullName}' is outside the neutral value algebra.");
        }
    }

    private static void WriteTagged(Utf8JsonWriter writer, string type, object value)
    {
        writer.WriteStartObject();
        writer.WriteString(TypeMarker, type);
        writer.WritePropertyName(TypedValue);
        switch (value)
        {
            case sbyte number: writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case ushort number: writer.WriteNumberValue(number); break;
            case int number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case float number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case string text: writer.WriteStringValue(text); break;
            default: throw new InvalidOperationException($"Unsupported tagged vector metadata value '{value.GetType().FullName}'.");
        }
        writer.WriteEndObject();
    }

    private static object? Read(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ReadObject(element),
        JsonValueKind.Array => new DataArray(element.EnumerateArray().Select(Read)),
        JsonValueKind.String => element.TryGetDateTimeOffset(out var date) ? date : element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException($"Vector provider metadata contains unsupported JSON kind '{element.ValueKind}'.")
    };

    private static object ReadObject(JsonElement element)
    {
        var properties = element.EnumerateObject().ToArray();
        if (properties.Length == 2 &&
            element.TryGetProperty(TypeMarker, out var marker) && marker.ValueKind == JsonValueKind.String &&
            element.TryGetProperty(TypedValue, out var value))
        {
            var text = marker.GetString();
            return text switch
            {
                "i8" => checked((sbyte)value.GetInt32()),
                "u8" => checked((byte)value.GetInt32()),
                "i16" => checked((short)value.GetInt32()),
                "u16" => checked((ushort)value.GetInt32()),
                "i32" => value.GetInt32(),
                "u32" => value.GetUInt32(),
                "i64" => value.GetInt64(),
                "u64" => value.GetUInt64(),
                "f32" => value.GetSingle(),
                "f64" => value.GetDouble(),
                "dec" => value.GetDecimal(),
                "guid" => Guid.ParseExact(value.GetString()!, "D"),
                "date" => DateOnly.ParseExact(value.GetString()!, "O", CultureInfo.InvariantCulture),
                "time" => TimeOnly.ParseExact(value.GetString()!, "O", CultureInfo.InvariantCulture),
                "datetime" => DateTime.ParseExact(value.GetString()!, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                "datetimeoffset" => DateTimeOffset.ParseExact(value.GetString()!, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                "duration" => TimeSpan.ParseExact(value.GetString()!, "c", CultureInfo.InvariantCulture),
                "bytes" => Convert.FromBase64String(value.GetString()!),
                _ => new DataObject(properties.Select(property => new DataProperty(property.Name, Read(property.Value))))
            };
        }
        return new DataObject(properties.Select(property => new DataProperty(property.Name, Read(property.Value))));
    }
}
