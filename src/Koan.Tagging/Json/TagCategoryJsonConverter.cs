using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Tagging.Json;

/// <summary>
/// Serialises a <see cref="TagCategory"/> as a flat JSON array of strings
/// (e.g. <c>["ffxiv", "sims4"]</c>) rather than as an object with enumeration metadata.
/// </summary>
public sealed class TagCategoryJsonConverter : JsonConverter<TagCategory>
{
    public override TagCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var cat = new TagCategory();
        if (reader.TokenType == JsonTokenType.Null) return cat;
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected start of array for {nameof(TagCategory)}, got {reader.TokenType}.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return cat;
            if (reader.TokenType == JsonTokenType.String)
            {
                var v = reader.GetString();
                if (!string.IsNullOrWhiteSpace(v)) cat.Set(v);
            }
        }
        throw new JsonException("Unterminated TagCategory array.");
    }

    public override void Write(Utf8JsonWriter writer, TagCategory value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value)
        {
            writer.WriteStringValue(v);
        }
        writer.WriteEndArray();
    }
}
