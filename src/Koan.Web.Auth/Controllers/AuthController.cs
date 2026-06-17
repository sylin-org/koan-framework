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
public sealed class AuthController(IProviderRegistry registry, IOptionsSnapshot<AuthOptions> authOptions, ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet(AuthConstants.Routes.Challenge)]
    public IActionResult Challenge([FromRoute] string provider, [FromQuery(Name = "return")] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(provider)) return NotFound();
        if (!registry.EffectiveProviders.TryGetValue(provider, out var cfg) || !cfg.Enabled) return NotFound();

        // SEC-0001 §10: the allow-list is the security boundary. Resolve the return URL HERE, before handing
        // it to the handler — the handler redirects to AuthenticationProperties.RedirectUri WITHOUT
        // re-validating it (an unvalidated pass-through would reintroduce an open redirect).
        var allowed = authOptions.Value.ReturnUrl.AllowList ?? [];
        var def = authOptions.Value.ReturnUrl.DefaultPath ?? "/";
        var ru = ReturnUrlPolicy.Resolve(returnUrl, allowed, def).Url;

        var props = new AuthenticationProperties { RedirectUri = ru };
        // Optional pass-through hint (e.g. prompt=login); the per-provider handler events forward it.
        var prompt = HttpContext.Request.Query.TryGetValue("prompt", out var p) ? p.ToString() : null;
        if (!string.IsNullOrWhiteSpace(prompt)) props.Items["prompt"] = prompt;

        logger.LogDebug("Auth challenge: provider={Provider} return={Return}", provider, ru);
        return Challenge(props, provider);
    }

    [HttpPost(AuthConstants.Routes.Logout)]
    [HttpGet(AuthConstants.Routes.Logout)]
    public async Task<IActionResult> Logout([FromQuery(Name = "return")] string? returnUrl)
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

        var allowed = authOptions.Value.ReturnUrl.AllowList ?? [];
        var def = authOptions.Value.ReturnUrl.DefaultPath ?? "/";
        // SEC-0001 §10: an allow-listed absolute return must use Redirect; LocalRedirect rejects any URL with a host.
        var (url, isLocal) = ReturnUrlPolicy.Resolve(returnUrl, allowed, def);
        return isLocal ? LocalRedirect(url) : Redirect(url);
    }
}
