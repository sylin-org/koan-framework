using System.Security.Claims;
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
/// action here) — (b) issues the same challenge carrying the signed-in person, whose callback binds the login it
/// verifies to them (SEC-0040), and (c) signs the user out.
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
        if (!TryProperties(returnUrl, failurePath, out var props, out var refused)) return refused;

        logger.LogDebug("Auth challenge: provider={Provider} return={Return}", provider, props.RedirectUri);
        return Challenge(props, provider);
    }

    /// <summary>
    /// SEC-0040 — binds a login of <paramref name="provider"/> to the person signed in: the provider's own challenge,
    /// carrying the person. Its callback binds the login it verifies to them, in place of any login of that provider they
    /// held, and refuses a login that is another person's. It is a form POST, which Koan's cross-site guard judges (SEC-0039)
    /// and a cross-site page cannot send with the session cookie, so binding never starts from another site; a connector
    /// that names an account reads it from the form. The person must have signed in within <c>ReConsent:MaxAge</c> unless
    /// <c>ReConsent:ForceOnLink</c> is turned off, so a cookie alone binds nothing.
    /// </summary>
    [HttpPost(AuthConstants.Routes.ConnectionsLink)]
    public async Task<IActionResult> Bind(
        [FromRoute] string provider,
        [FromForm(Name = AuthConstants.Query.Return)] string? returnUrl,
        [FromForm(Name = AuthConstants.Query.Failure)] string? failurePath)
    {
        if (string.IsNullOrWhiteSpace(provider)) return NotFound();
        if (providers.Find(provider) is not { Eligible: true }) return NotFound();
        if (!TryProperties(returnUrl, failurePath, out var props, out var refused)) return refused;

        var current = await HttpContext.AuthenticateAsync(AuthenticationExtensions.CookieScheme);
        var person = current.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? current.Principal?.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(person))
            return SignInFirst(props, "Sign in first: binding adds a login to the person signed in.");
        var reConsent = authOptions.Value.ReConsent;
        if (reConsent.ForceOnLink && !SignInTime.Within(HttpContext, current.Properties, reConsent.MaxAge))
            return SignInFirst(props, "Sign in again first: binding a login needs a recent sign-in.");

        props.Items[AuthConstants.Items.Bind] = person;
        logger.LogDebug("Auth bind challenge: provider={Provider} return={Return}", provider, props.RedirectUri);
        return Challenge(props, provider);
    }

    /// <summary>The properties a challenge carries to its callback, or the refusal of a failure landing that is not one.</summary>
    private bool TryProperties(string? returnUrl, string? failurePath, out AuthenticationProperties props, out IActionResult refused)
    {
        // SEC-0001 §10 / SEC-0029: the allow-list is the security boundary. Resolve the return URL HERE, before
        // handing it to the handler — the handler redirects to AuthenticationProperties.RedirectUri WITHOUT
        // re-validating it. Only a local path under the application or an allow-listed absolute URL passes;
        // anything else lands on the default landing under the base path (SEC-0027), never on the value.
        props = new AuthenticationProperties { RedirectUri = ReturnUrlPolicy.Resolve(HttpContext, returnUrl, authOptions.Value.ReturnUrl).Url };
        refused = new EmptyResult();
        // WEB-0080: the failure landing is a rooted path of this application, carried to the handler so a sign-in
        // that cannot complete sends the person there with a sanitized code. Anything else is refused here.
        if (failurePath is not null)
        {
            if (!SignInFailureReturn.TryAccept(failurePath, out var landing, out var problem))
            {
                refused = BadRequest($"'{AuthConstants.Query.Failure}' must be a rooted path of this application, such as /sign-in, where a failed sign-in lands; {problem}.");
                return false;
            }
            props.Items[AuthConstants.Items.FailurePath] = landing;
        }
        // Optional pass-through hint (e.g. prompt=login); the per-provider handler events forward it.
        var prompt = HttpContext.Request.Query.TryGetValue("prompt", out var p) ? p.ToString() : null;
        if (!string.IsNullOrWhiteSpace(prompt)) props.Items["prompt"] = prompt;
        return true;
    }

    /// <summary>A binding that needs a (recent) sign-in first: the failure landing with its code, or a 401 Problem saying why.</summary>
    private IActionResult SignInFirst(AuthenticationProperties props, string why)
        => SignInFailureReturn.Redirect(HttpContext, props, SignInFailure.SignInFirst)
            ? new EmptyResult()
            : SignInFailureReturn.Problem(SignInFailure.SignInFirst, why).ToResult();

    // POST only (SEC-0035): on a GET, an image in anything a person reads would end their session.
    [HttpPost(AuthConstants.Routes.Logout)]
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
