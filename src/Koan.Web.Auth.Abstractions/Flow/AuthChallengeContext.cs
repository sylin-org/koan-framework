using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Flow;

/// <summary>
/// Context passed to <see cref="IKoanAuthFlowHandler.OnChallenge"/>. Wraps the cookie middleware's
/// redirect-to-login event with a mutable, handler-friendly surface: rewrite the URL, set a status
/// code directly, or mark the response handled to suppress the default redirect.
/// </summary>
/// <remarks>
/// <para>
/// <b>Default behavior</b> when no handler marks <see cref="ResponseHandled"/>: the framework
/// emits a 302 to <see cref="RedirectUri"/>. The initial value of <see cref="RedirectUri"/> is
/// what the cookie middleware computed (typically <c>/sign-in?ReturnUrl=…</c>); handlers can
/// replace it with anything (e.g. a provider-specific challenge endpoint).
/// </para>
/// <para>
/// <b>For JSON / XHR responses</b>, a handler typically sets
/// <c>HttpContext.Response.StatusCode = 401</c> and marks <see cref="ResponseHandled"/>. Koan
/// ships <c>JsonChallengeHandler</c> that does exactly this for requests matching configured API
/// path patterns plus the <c>Accept: application/json</c> + <c>X-Requested-With: XMLHttpRequest</c>
/// heuristics.
/// </para>
/// </remarks>
public sealed class AuthChallengeContext
{
    /// <summary>The originating request + response.</summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>Per-request service provider.</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// The redirect URI the cookie middleware would have used. Treated as a snapshot — handlers
    /// inspect this when deciding whether to rewrite <see cref="RedirectUri"/>.
    /// </summary>
    public required string DefaultRedirectUri { get; init; }

    /// <summary>
    /// Mutable redirect target. Initial value mirrors <see cref="DefaultRedirectUri"/>. Handlers
    /// may rewrite this; the framework emits a 302 here only when no handler sets
    /// <see cref="ResponseHandled"/>.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Set to true when a handler has fully shaped the response (status code, headers, body). The
    /// framework suppresses its default redirect emission when this is true. Subsequent handlers
    /// still run, but should treat the response as final.
    /// </summary>
    public bool ResponseHandled { get; set; }
}
