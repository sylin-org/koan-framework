using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;

// Newtonsoft.Json is deliberately not imported: it also defines JsonConverter/JsonException, and this
// file needs the System.Text.Json ones. The single Newtonsoft member used is qualified inline.

namespace Koan.Mcp.Hosting;

/// <summary>
/// Lets System.Text.Json read and write Newtonsoft <see cref="JToken"/> trees on the JSON-RPC wire.
/// </summary>
/// <remarks>
/// MCP payloads are modelled with Newtonsoft throughout the module — tool arguments, input schemas,
/// capabilities, annotations, and diagnostics are all <see cref="JObject"/> or <see cref="JToken"/> —
/// while the STDIO transport frames messages with System.Text.Json so the <c>[JsonPropertyName]</c>
/// contract on the RPC DTOs stays authoritative.
///
/// Without this bridge the two halves silently disagree, in both directions:
///   reading  — STJ cannot construct a JObject, so every <c>tools/call</c> carrying arguments fails
///              with "The JSON value could not be converted to Newtonsoft.Json.Linq.JToken";
///   writing  — STJ reflects over JObject's internals instead of emitting its value, collapsing every
///              advertised schema to <c>"properties":[[[[]]]]</c> and capabilities to <c>[[[]]]</c>.
///
/// The HTTP transport is unaffected because it serializes with Newtonsoft end to end.
/// </remarks>
internal sealed class JTokenJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeof(JToken).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(JTokenJsonConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class JTokenJsonConverter<TToken> : JsonConverter<TToken?>
    where TToken : JToken
{
    public override TToken? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        using var document = JsonDocument.ParseValue(ref reader);
        var token = JToken.Parse(document.RootElement.GetRawText());
        if (token is TToken typed) return typed;

        throw new JsonException(
            $"Expected a JSON value convertible to {typeof(TToken).Name}, but the payload was {token.Type}.");
    }

    public override void Write(Utf8JsonWriter writer, TToken? value, JsonSerializerOptions options)
    {
        if (value is null || value.Type == JTokenType.Null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteRawValue(value.ToString(Newtonsoft.Json.Formatting.None));
    }
}
