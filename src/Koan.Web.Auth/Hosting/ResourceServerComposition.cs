using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// SEC-0025 — makes the resource edge the default authenticate, challenge and forbid scheme when the application says
/// it is a resource server (<c>Koan:Web:Auth:ResourceServer:Enabled</c>). The default scheme, sign-in and sign-out stay
/// with the session cookie, which the edge forwards every request without an <c>Authorization</c> header to.
/// </summary>
internal sealed class ResourceServerComposition(IOptions<AuthOptions> auth) : IPostConfigureOptions<AuthenticationOptions>
{
    public void PostConfigure(string? name, AuthenticationOptions options)
    {
        if (!auth.Value.ResourceServer.Enabled) return;
        options.DefaultAuthenticateScheme = AuthenticationExtensions.ResourceServerScheme;
        options.DefaultChallengeScheme = AuthenticationExtensions.ResourceServerScheme;
        options.DefaultForbidScheme = AuthenticationExtensions.ResourceServerScheme;
    }
}
