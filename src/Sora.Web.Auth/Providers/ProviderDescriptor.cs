using Newtonsoft.Json;

namespace Sora.Web.Auth.Providers;

public sealed class ProviderDescriptor
{
    [JsonProperty("id")] public required string Id { get; init; }
    [JsonProperty("name")] public required string Name { get; init; }
    [JsonProperty("protocol")] public required string Protocol { get; init; }
    [JsonProperty("enabled")] public bool Enabled { get; init; }
    [JsonProperty("state")] public string State { get; init; } = "Unknown"; // Healthy | Unhealthy | Unknown
    [JsonProperty("icon")] public string? Icon { get; init; }
    [JsonProperty("challengeUrl")] public string? ChallengeUrl { get; init; }
    [JsonProperty("metadataUrl")] public string? MetadataUrl { get; init; }
    [JsonProperty("scopes")] public string[]? Scopes { get; init; }
}