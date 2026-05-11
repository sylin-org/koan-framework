using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Tagging.Json;

/// <summary>
/// Serialises a <see cref="TagScope"/> as a JSON object where each property is a category name
/// and the value is an array of tag strings:
/// <code>
/// { "game": ["ffxiv"], "technique": ["dof", "clarity"] }
/// </code>
/// Empty categories are stripped from output to keep payloads clean.
/// </summary>
public sealed class TagScopeJsonConverter : JsonConverter<TagScope>
{
    public override TagScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var scope = new TagScope();
        if (reader.TokenType == JsonTokenType.Null) return scope;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected start of object for {nameof(TagScope)}, got {reader.TokenType}.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return scope;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected property name in {nameof(TagScope)}, got {reader.TokenType}.");

            var categoryName = reader.GetString();
            if (string.IsNullOrWhiteSpace(categoryName))
                throw new JsonException("TagScope category names cannot be empty.");

            reader.Read();
            var cat = JsonSerializer.Deserialize<TagCategory>(ref reader, options);
            if (cat is not null)
            {
                var live = scope[categoryName];
                foreach (var t in cat) live.Set(t);
            }
        }
        throw new JsonException("Unterminated TagScope object.");
    }

    public override void Write(Utf8JsonWriter writer, TagScope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (name, cat) in value.Categories)
        {
            // Strip empty categories so output stays tight.
            if (cat.Count == 0) continue;
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, cat, options);
        }
        writer.WriteEndObject();
    }
}
