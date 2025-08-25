using System.Text.Json.Serialization;

namespace Sora.Web.Auth.Providers;

public sealed class ProviderDescriptor
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("protocol")] public required string Protocol { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = "Unknown"; // Healthy | Unhealthy | Unknown
    [JsonPropertyName("icon")] public string? Icon { get; init; }
    [JsonPropertyName("challengeUrl")] public string? ChallengeUrl { get; init; }
    [JsonPropertyName("metadataUrl")] public string? MetadataUrl { get; init; }
    [JsonPropertyName("scopes")] public string[]? Scopes { get; init; }
}