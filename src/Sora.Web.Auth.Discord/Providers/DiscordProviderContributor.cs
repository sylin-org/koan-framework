using System.Collections.Generic;
using Sora.Web.Auth.Infrastructure;
using Sora.Web.Auth.Options;
using Sora.Web.Auth.Providers;

namespace Sora.Web.Auth.Discord;

internal sealed class DiscordProviderContributor : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, ProviderOptions> GetDefaults()
    {
        return new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["discord"] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.OAuth2,
                DisplayName = "Discord",
                Icon = "/icons/discord.svg",
                AuthorizationEndpoint = "https://discord.com/api/oauth2/authorize",
                TokenEndpoint = "https://discord.com/api/oauth2/token",
                UserInfoEndpoint = "https://discord.com/api/users/@me",
                Scopes = new []{"identify","email"},
                Enabled = true
            }
        };
    }
}
