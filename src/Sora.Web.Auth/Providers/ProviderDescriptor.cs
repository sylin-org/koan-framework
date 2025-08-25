using Sora.Web.Auth.Infrastructure;
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

internal static class ProviderDescriptorFactory
{
    public static ProviderDescriptor Create(string id, string name, string protocol, bool enabled, string state, string? icon, string[]? scopes)
    {
        string? challenge = protocol is AuthConstants.Protocols.Oidc or AuthConstants.Protocols.OAuth2
            ? $"/auth/{id}/challenge"
            : null;
        string? metadata = protocol == AuthConstants.Protocols.Saml ? $"/auth/{id}/saml/metadata" : null;
        return new ProviderDescriptor
        {
            Id = id,
            Name = name,
            Protocol = protocol,
            Enabled = enabled,
            State = state,
            Icon = icon,
            ChallengeUrl = challenge,
            MetadataUrl = metadata,
            Scopes = scopes
        };
    }
}
