using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using System.Collections.Generic;

namespace Koan.Web.Auth.Connector.Microsoft.Providers;

internal sealed class MicrosoftProviderContributor : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, ProviderOptions> GetDefaults()
        => AuthProviderDefaults.Oidc("microsoft", "Microsoft", "/icons/microsoft.svg",
            "https://login.microsoftonline.com/common/v2.0", new[] { "openid", "email", "profile" }, priority: 200);
}

