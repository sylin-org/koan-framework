using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Connector.Discord.Providers;

internal sealed class DiscordProviderContributor : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, ProviderOptions> GetDefaults()
        => AuthProviderDefaults.OAuth2("discord", "Discord", "/icons/discord.svg",
            "https://discord.com/api/oauth2/authorize", "https://discord.com/api/oauth2/token", "https://discord.com/api/users/@me",
            new[] { "identify", "email" }, priority: 150);
}

