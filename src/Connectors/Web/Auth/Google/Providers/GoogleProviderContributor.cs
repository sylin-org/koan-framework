using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using System.Collections.Generic;

namespace Koan.Web.Auth.Connector.Google.Providers;

internal sealed class GoogleProviderContributor : IAuthProviderContributor
{
    public IReadOnlyDictionary<string, ProviderOptions> GetDefaults()
        => AuthProviderDefaults.Oidc("google", "Google", "/icons/google.svg",
            "https://accounts.google.com", new[] { "openid", "email", "profile" }, priority: 200);
}

