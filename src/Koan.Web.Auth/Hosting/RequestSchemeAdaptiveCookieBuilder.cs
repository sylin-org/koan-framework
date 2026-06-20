using System;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Hosting;

/// <summary>
/// WEB-0071 — a correlation/nonce cookie whose SameSite/Secure policy adapts to the APP'S FRONT-CHANNEL request
/// scheme at write time, not to a provider's back-channel endpoint scheme guessed at boot.
/// </summary>
/// <remarks>
/// The maintained OAuth/OIDC handler writes the correlation (and OIDC nonce) cookie during the challenge via
/// <see cref="CookieBuilder.Build(HttpContext, DateTimeOffset)"/>, which receives the LIVE request — so whether the
/// browser will store the cookie depends on whether the app's request is https (X-Forwarded-Proto-aware once the
/// app has run <c>UseForwardedHeaders</c>), NOT on the provider's token endpoint / OIDC authority scheme. A
/// <c>SameSite=None</c> cookie MUST be <c>Secure</c> (RFC 6265bis) or the browser silently drops it — which surfaces
/// as "Correlation failed" on the callback. So over plain http we relax to <c>SameSite=Lax</c> (which is sent on the
/// top-level GET callback navigation and does not require <c>Secure</c>); over https we keep the framework default
/// (None + Secure, which also supports a cross-site form_post response). This adapts per request, so the same scheme
/// works for a real https provider behind a plain-http dev host AND a production https deployment, with no per-
/// provider branching and no boot-time scheme guess.
/// </remarks>
internal sealed class RequestSchemeAdaptiveCookieBuilder : CookieBuilder
{
    /// <summary>Create an adaptive builder carrying the same configuration as an existing cookie builder.</summary>
    public static RequestSchemeAdaptiveCookieBuilder Wrap(CookieBuilder template) => new()
    {
        Name = template.Name,
        Path = template.Path,
        Domain = template.Domain,
        HttpOnly = template.HttpOnly,
        SameSite = template.SameSite,
        SecurePolicy = template.SecurePolicy,
        IsEssential = template.IsEssential,
        Expiration = template.Expiration,
        MaxAge = template.MaxAge,
    };

    public override CookieOptions Build(HttpContext context, DateTimeOffset expiresFrom)
    {
        var options = base.Build(context, expiresFrom);
        // Front-channel request, not the provider endpoint, decides storability. Over plain http a SameSite=None
        // cookie can't be Secure, so the browser would drop it — relax to Lax (sufficient for the GET callback).
        if (context is not null && !context.Request.IsHttps)
        {
            options.SameSite = SameSiteMode.Lax;
            options.Secure = false;
        }
        return options;
    }
}
