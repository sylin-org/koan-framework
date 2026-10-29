using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Controllers;

/// <summary>
/// WEB-0071 — the OAuth2/OIDC challenge+callback are owned by the maintained ASP.NET handlers
/// (one scheme per effective provider, seeded by <see cref="Hosting.AuthSchemeSeeder"/>). This controller
/// now only (a) issues the framework challenge after the return-url allow-list runs — the handler then
/// builds the authorize URL, sends PKCE, manages state/nonce/correlation, validates the id_token, and
/// intercepts <c>/auth/{provider}/callback</c> as RemoteAuthentication middleware (so there is no callback
/// action here) — and (b) signs the user out.
/// </summary>
[ApiController]
public sealed class AuthController(IAuthProviderCatalog providers, IOptions<AuthOptions> authOptions, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet(AuthConstants.Routes.Challenge)]
    public IActionResult Challenge(
        [FromRoute] string provider,
        [FromQuery(Name = AuthConstants.Query.Return)] string? returnUrl,
        [FromQuery(Name = AuthConstants.Query.Failure)] string? failurePath)
    {
        if (string.IsNullOrWhiteSpace(provider)) return NotFound();
        if (providers.Find(provider) is not { Eligible: true }) return NotFound();

        // SEC-0001 §10 / SEC-0029: the allow-list is the security boundary. Resolve the return URL HERE, before
        // handing it to the handler — the handler redirects to AuthenticationProperties.RedirectUri WITHOUT
        // re-validating it. Only a local path under the application or an allow-listed absolute URL passes;
        // anything else lands on the default landing under the base path (SEC-0027), never on the value.
        var ru = ReturnUrlPolicy.Resolve(HttpContext, returnUrl, authOptions.Value.ReturnUrl).Url;

        var props = new AuthenticationProperties { RedirectUri = ru };
        // WEB-0080: the failure landing is a rooted path of this application, carried to the handler so a sign-in
        // that cannot complete sends the person there with a sanitized code. Anything else is refused here.
        if (failurePath is not null)
        {
            if (!SignInFailureReturn.TryAccept(failurePath, out var landing, out var problem))
                return BadRequest($"'{AuthConstants.Query.Failure}' must be a rooted path of this application, such as /sign-in, where a failed sign-in lands; {problem}.");
            props.Items[AuthConstants.Items.FailurePath] = landing;
        }
        // Optional pass-through hint (e.g. prompt=login); the per-provider handler events forward it.
        var prompt = HttpContext.Request.Query.TryGetValue("prompt", out var p) ? p.ToString() : null;
        if (!string.IsNullOrWhiteSpace(prompt)) props.Items["prompt"] = prompt;

        logger.LogDebug("Auth challenge: provider={Provider} return={Return}", provider, ru);
        return Challenge(props, provider);
    }

    [HttpPost(AuthConstants.Routes.Logout)]
    [HttpGet(AuthConstants.Routes.Logout)]
    public async Task<IActionResult> Logout([FromQuery(Name = AuthConstants.Query.Return)] string? returnUrl)
    {
        await HttpContext.SignOutAsync(AuthenticationExtensions.CookieScheme);
        // Best-effort: also clear the local dev TestProvider cookie to avoid silent re-login loops.
        try
        {
            Response.Cookies.Append(AuthConstants.Dev.TestProviderCookieUser, "", new CookieOptions
            {
                Expires = DateTimeOffset.UnixEpoch,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                Secure = Request.IsHttps
            });
        }
        catch { /* ignore */ }

        // SEC-0001 §10: an allow-listed absolute return must use Redirect; LocalRedirect rejects any URL with a host.
        // SEC-0029: a refused value lands on the default landing, so LocalRedirect only ever sees a local path.
        var (url, isLocal) = ReturnUrlPolicy.Resolve(HttpContext, returnUrl, authOptions.Value.ReturnUrl);
        return isLocal ? LocalRedirect(url) : Redirect(url);
    }
}
