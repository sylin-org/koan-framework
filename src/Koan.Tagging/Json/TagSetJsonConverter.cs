using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Tagging.Json;

/// <summary>
/// Serialises a <see cref="TagSet"/> as a JSON object with two top-level properties:
/// <code>
/// { "public": { ...categories... }, "private": { ...categories... } }
/// </code>
/// Property names are lowercase (<c>public</c> / <c>private</c>) by convention; this is
/// the admin / curator surface representation. Public APIs typically project to a flat
/// string array via <see cref="TagSet.PublicTags"/> rather than emitting the full TagSet.
/// </summary>
public sealed class TagSetJsonConverter : JsonConverter<TagSet>
{
    private const string PublicProperty = "public";
    private const string PrivateProperty = "private";

    public override TagSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var set = new TagSet();
        if (reader.TokenType == JsonTokenType.Null) return set;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected start of object for {nameof(TagSet)}, got {reader.TokenType}.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return set;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected property name in {nameof(TagSet)}, got {reader.TokenType}.");

            var prop = reader.GetString();
            reader.Read();

            if (string.Equals(prop, PublicProperty, StringComparison.OrdinalIgnoreCase))
            {
                CopyScope(JsonSerializer.Deserialize<TagScope>(ref reader, options), set.Public);
            }
            else if (string.Equals(prop, PrivateProperty, StringComparison.OrdinalIgnoreCase))
            {
                CopyScope(JsonSerializer.Deserialize<TagScope>(ref reader, options), set.Private);
            }
            else
            {
                // Unknown property — skip it (forward-compat).
                reader.Skip();
            }
        }
        throw new JsonException("Unterminated TagSet object.");
    }

    public override void Write(Utf8JsonWriter writer, TagSet value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(PublicProperty);
        JsonSerializer.Serialize(writer, value.Public, options);
        writer.WritePropertyName(PrivateProperty);
        JsonSerializer.Serialize(writer, value.Private, options);
        writer.WriteEndObject();
    }

    private static void CopyScope(TagScope? source, TagScope target)
    {
        if (source is null) return;
        foreach (var (name, cat) in source.Categories)
        {
            var live = target[name];
            foreach (var t in cat) live.Set(t);
        }
    }
}
