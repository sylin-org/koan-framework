using Koan.Core;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Koan.Web.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Connector.Atproto.Hosting;

/// <summary>
/// WEB-0081: an eligible provider whose client identity derives from the base URL cannot use an origin that names no
/// client, so the application never accepts one. An explicit <c>ClientId</c> does not derive from it, and a provider
/// that is not eligible publishes nothing.
/// </summary>
internal sealed class AtprotoApplicationUrlRule(IOptions<AuthOptions> auth, IAuthProviderCatalog catalog, IHostEnvironment environment) : IApplicationUrlRule
{
    public string? Refuses(string origin)
    {
        if (auth.Value.Providers.GetValueOrDefault(Constants.Provider)?.ClientId is not null) return null;
        if (catalog.Find(Constants.Provider) is not { Eligible: true } provider) return null;
        return AtprotoClientIdentity.Derive(origin, provider.Scopes, KoanEnv.Gate.DevelopmentOnly(environment)) is null
            ? AtprotoClientIdentity.OriginRefused
            : null;
    }
}
