using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Data.Abstractions;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal static class SqliteStructuredValues
{
    public static string Serialize(object? value) =>
        ToNode(value)?.ToJsonString() ?? "null";

    public static object? Deserialize(object? value)
    {
        if (value is null or DBNull) return null;
        if (value is not string json)
            throw new InvalidDataException($"SQLite structured value has unsupported type '{value.GetType().FullName}'.");
        using var document = JsonDocument.Parse(json);
        return FromElement(document.RootElement);
    }

    public static object? ReadPath(object? root, IReadOnlyList<string> segments)
    {
        object? current = root;
        foreach (var segment in segments)
        {
            if (current is not DataObject item) return null;
            var matches = item.Properties.Where(property =>
                string.Equals(property.Name, segment, StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException($"SQLite structured value has duplicate property '{segment}'.");
            if (matches.Length == 0) return null;
            current = matches[0].Value;
        }
        return current;
    }

    public static JsonObject Build(IEnumerable<(IReadOnlyList<string> Path, object? Value)> values)
    {
        var root = new JsonObject();
        foreach (var (path, value) in values)
        {
            if (path.Count == 0) throw new ArgumentException("A nested SQLite value requires a physical path.", nameof(values));
            JsonObject current = root;
            for (var index = 0; index < path.Count - 1; index++)
            {
                var segment = path[index];
                if (current[segment] is null) current[segment] = new JsonObject();
                current = current[segment] as JsonObject
                    ?? throw new InvalidDataException($"SQLite structured path '{string.Join('/', path)}' overlaps a scalar value.");
            }
            current[path[^1]] = ToNode(value);
        }
        return root;
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        DataObject item => Object(item),
        DataArray array => new JsonArray(array.Items.Select(ToNode).ToArray()),
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        _ => JsonSerializer.SerializeToNode(value, value.GetType())
    };

    private static JsonObject Object(DataObject value)
    {
        var result = new JsonObject();
        foreach (var property in value.Properties)
        {
            if (result.ContainsKey(property.Name))
                throw new InvalidDataException($"SQLite JSON cannot preserve duplicate property '{property.Name}'.");
            result[property.Name] = ToNode(property.Value);
        }
        return result;
    }

    private static object? FromElement(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.Array => new DataArray(value.EnumerateArray().Select(FromElement)),
        JsonValueKind.Object => new DataObject(value.EnumerateObject()
            .Select(property => new DataProperty(property.Name, FromElement(property.Value)))),
        _ => throw new InvalidDataException($"Unsupported SQLite JSON token '{value.ValueKind}'.")
    };
}
