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
        // SEC-0003 §2.2: the TestProvider IS the zero-config dev login — available in Development with no config
        // (pick a profile → a real signed session). Opt-in outside Development. (The `?_as=` trust override is the
        // separate, transient quick-test path; the default everywhere is anonymous.)
        var enabled = o.Enabled || env.IsDevelopment() || o.ExposeInDiscoveryOutsideDevelopment;
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
            },
            ["test-oidc"] = new Koan.Web.Auth.Options.ProviderOptions
            {
                Type = Koan.Web.Auth.Infrastructure.AuthConstants.Protocols.Oidc,
                DisplayName = "Test OIDC (Local)",
                Icon = "/icons/test.svg",
                // Relative base; the scheme seeder (WEB-0071) resolves it to the in-network absolute Authority.
                Authority = "/.testoauth",
                ClientId = o.ClientId,
                ClientSecret = o.ClientSecret,
                Scopes = new[] { "openid", "profile", "email" },
                Enabled = true,
                Priority = env.IsDevelopment() ? 26 : -100
            }
        };
    }
}
