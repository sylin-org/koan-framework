using Koan.Web.Auth.Infrastructure;

namespace Koan.Web.Auth.Providers;

internal static class ProviderDescriptorFactory
{
    public static ProviderDescriptor Create(string id, string name, string protocol, bool enabled, string state, string? icon, string[]? scopes, int priority)
    {
        string? challenge = protocol is AuthConstants.Protocols.Oidc or AuthConstants.Protocols.OAuth2
            ? $"/auth/{id}/challenge"
            : null;
        string? metadata = null; // SAML excised (E5); MetadataUrl retained on the DTO for wire-compat, always null
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
            Scopes = scopes,
            Priority = priority
        };
    }
}