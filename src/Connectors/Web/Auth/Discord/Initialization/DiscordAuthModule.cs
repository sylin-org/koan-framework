using Koan.Core;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Auth.Connector.Discord.Initialization;

public sealed class DiscordAuthModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddSingleton(AuthProviderDefinition.OAuth2(
            "discord",
            "Discord",
            "/icons/discord.svg",
            "https://discord.com/api/oauth2/authorize",
            "https://discord.com/api/oauth2/token",
            "https://discord.com/api/users/@me",
            ["identify", "email"],
            priority: 150));

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(Version);
}
