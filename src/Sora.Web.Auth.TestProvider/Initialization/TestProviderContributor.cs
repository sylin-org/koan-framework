using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sora.Web.Auth.Providers;
using Sora.Web.Auth.TestProvider.Options;

namespace Sora.Web.Auth.TestProvider.Initialization;

internal sealed class TestProviderContributor(IConfiguration cfg, IHostEnvironment env) : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, Sora.Web.Auth.Options.ProviderOptions> GetDefaults()
    {
        var o = cfg.GetSection(TestProviderOptions.SectionPath).Get<TestProviderOptions>() ?? new TestProviderOptions();
        var enabled = env.IsDevelopment() || o.Enabled || o.ExposeInDiscoveryOutsideDevelopment;
        if (!enabled) return new Dictionary<string, Sora.Web.Auth.Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, Sora.Web.Auth.Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = new Sora.Web.Auth.Options.ProviderOptions
            {
                Type = Sora.Web.Auth.Infrastructure.AuthConstants.Protocols.OAuth2,
                DisplayName = "Test (Local)",
                Icon = "/icons/test.svg",
                AuthorizationEndpoint = "/.testoauth/authorize",
                TokenEndpoint = "/.testoauth/token",
                UserInfoEndpoint = "/.testoauth/userinfo",
                ClientId = o.ClientId,
                ClientSecret = o.ClientSecret,
                Scopes = new []{ "identify", "email" },
                Enabled = true
            }
        };
    }
}