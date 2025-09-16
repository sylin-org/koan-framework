using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using System.Collections.Generic;

namespace Koan.Web.Auth.Microsoft.Providers;

internal sealed class MicrosoftProviderContributor : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, ProviderOptions> GetDefaults()
    {
        return new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["microsoft"] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.Oidc,
                DisplayName = "Microsoft",
                Icon = "/icons/microsoft.svg",
                Authority = "https://login.microsoftonline.com/common/v2.0",
                Scopes = new[] { "openid", "email", "profile" },
                Enabled = true
            }
        };
    }
}
