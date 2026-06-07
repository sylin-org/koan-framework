using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Koan.Web.Auth.Providers;
using Koan.Web.Auth.Connector.Test.Options;

namespace Koan.Web.Auth.Connector.Test.Initialization;

internal sealed class TestProviderContributor(IConfiguration cfg, IHostEnvironment env) : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, Koan.Web.Auth.Options.ProviderOptions> GetDefaults()
    {
        var o = cfg.GetSection(TestProviderOptions.SectionPath).Get<TestProviderOptions>() ?? new TestProviderOptions();
        // SEC-0001 2h: opt-in only — the TestProvider no longer auto-enables in Development. The everyday
        // dev login is the zero-config trust identity (Rung 0); this provider is the opt-in OAuth-flow simulator.
        var enabled = o.Enabled || o.ExposeInDiscoveryOutsideDevelopment;
        if (!enabled) return new Dictionary<string, Koan.Web.Auth.Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, Koan.Web.Auth.Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = new Koan.Web.Auth.Options.ProviderOptions
            {
                Type = Koan.Web.Auth.Infrastructure.AuthConstants.Protocols.OAuth2,
                DisplayName = "Test (Local)",
                Icon = "/icons/test.svg",
                AuthorizationEndpoint = "/.testoauth/authorize",
                TokenEndpoint = "/.testoauth/token",
                UserInfoEndpoint = "/.testoauth/userinfo",
                ClientId = o.ClientId,
                ClientSecret = o.ClientSecret,
                Scopes = new[] { "identify", "email" },
                Enabled = true,
                Priority = env.IsDevelopment() ? 25 : -100
            }
        };
    }
}
