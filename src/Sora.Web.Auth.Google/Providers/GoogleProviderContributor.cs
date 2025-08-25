using System.Collections.Generic;
using Sora.Web.Auth.Infrastructure;
using Sora.Web.Auth.Options;
using Sora.Web.Auth.Providers;

namespace Sora.Web.Auth.Google;

internal sealed class GoogleProviderContributor : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, ProviderOptions> GetDefaults()
    {
        return new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["google"] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.Oidc,
                DisplayName = "Google",
                Icon = "/icons/google.svg",
                Authority = "https://accounts.google.com",
                Scopes = new []{"openid","email","profile"},
                Enabled = true
            }
        };
    }
}
