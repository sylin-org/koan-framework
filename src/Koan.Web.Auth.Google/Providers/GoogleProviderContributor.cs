using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using System.Collections.Generic;

namespace Koan.Web.Auth.Google.Providers;

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
                Scopes = new[] { "openid", "email", "profile" },
                Enabled = true
            }
        };
    }
}
