using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// SEC-0025 — a request that carries a <c>Bearer</c> <c>Authorization</c> header the resource edge did not accept is answered 401
/// with <c>WWW-Authenticate: Bearer error="invalid_token"</c> before the action runs, even where the action admits
/// anonymous callers: an agent learns that its token is expired or not this application's instead of reading as a
/// visitor. Applies to MVC controller actions and Razor Pages while the application is a resource server, except those
/// marked <see cref="ResourceServerExemptAttribute"/> — the authorization server's protocol endpoints, which never
/// authenticate by the application's bearer and give their own protocol answers, and a provider's endpoints that check a
/// token of their own. A header of another scheme is left to the session. Endpoints outside MVC answer in their own terms.
/// </summary>
internal sealed class RejectedBearerFilter(IOptions<AuthOptions> auth) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!auth.Value.ResourceServer.Enabled || context.Result is not null) return;
        if (context.ActionDescriptor.EndpointMetadata.Any(static metadata => metadata is ResourceServerExemptAttribute)) return;
        var http = context.HttpContext;
        if (!ResourceServerHandler.CarriesBearer(http.Request)) return;
        if ((await http.AuthenticateAsync(AuthenticationExtensions.ResourceServerScheme).ConfigureAwait(false)).Succeeded) return;
        context.Result = new ChallengeResult(AuthenticationExtensions.ResourceServerScheme);
    }
}
