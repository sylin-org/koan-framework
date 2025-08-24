using System.Collections.Generic;
using Sora.Web.Auth.Infrastructure;
using Sora.Web.Auth.Options;
using Sora.Web.Auth.Providers;

namespace Sora.Web.Auth.Microsoft;

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
                Scopes = new []{"openid","email","profile"},
                Enabled = true
            }
        };
    }
}
