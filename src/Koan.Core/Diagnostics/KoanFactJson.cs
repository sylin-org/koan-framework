using System.Text.Json.Serialization;

namespace Koan.Core.Diagnostics;

/// <summary>The serialization authority for the public runtime-fact envelope.</summary>
public static class KoanFactJson
{
    public static string Serialize(KoanFactEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return System.Text.Json.JsonSerializer.Serialize(envelope, KoanFactJsonContext.Default.KoanFactEnvelope);
    }

    public static KoanFactEnvelope? Deserialize(string json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize(json, KoanFactJsonContext.Default.KoanFactEnvelope);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(KoanFactEnvelope))]
internal sealed partial class KoanFactJsonContext : JsonSerializerContext;
