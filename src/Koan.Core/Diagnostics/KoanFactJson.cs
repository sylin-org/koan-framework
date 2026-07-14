using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Core.Diagnostics;

/// <summary>The serialization authority for the public runtime-fact envelope.</summary>
public static class KoanFactJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(KoanFactEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static KoanFactEnvelope? Deserialize(string json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<KoanFactEnvelope>(json, Options);
}
