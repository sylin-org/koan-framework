namespace Koan.Web.Auth.Server.Options;

/// <summary>
/// SEC-0006 — configuration for the embedded OAuth 2.1 Authorization Server. Bound from
/// <c>Koan:Web:Auth:Server</c>. Phase 1 carries only the dev-token convenience knobs; the authorize/token/DCR
/// surface lands in later phases.
/// </summary>
public sealed class AuthServerOptions
{
    public const string SectionPath = "Koan:Web:Auth:Server";

    /// <summary>
    /// Whether the <c>/oauth/dev-token</c> convenience endpoint is mapped. It is <b>additionally</b> hard-gated
    /// to the Development environment at request time (it returns 404 anywhere else, even if this is true), so a
    /// production build can never mint a token from a cookie session without the full authorization-code flow.
    /// </summary>
    public bool DevTokenEnabled { get; set; } = true;

    /// <summary>Lifetime of a dev-token, in minutes.</summary>
    public int DevTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// SEC-0006 D7 — the app's consent page (it renders the requesting client + scopes + Allow/Deny). The AS
    /// redirects the browser here with <c>?rid=…</c> after <c>/oauth/authorize</c>. The app owns rendering; the
    /// framework owns the protocol. Defaults under the app's <c>/me/…</c> namespace.
    /// </summary>
    public string ConsentPath { get; set; } = "/me/connect";

    /// <summary>SEC-0006 — the app's terminal "you can close this page now" page (device flow lands here).</summary>
    public string DonePath { get; set; } = "/me/connect/done";

    /// <summary>SEC-0006 D4 — authorization-code lifetime. Must be short (RFC 6749 §4.1.2 recommends ≤ 10 min; we cap tighter).</summary>
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>SEC-0006 D7 — how long a pending consent request (<c>rid</c>) is valid before it must be restarted.</summary>
    public TimeSpan ConsentRequestLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>SEC-0006 D6 / token — issued access-token lifetime.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// SEC-0006 D1 — how long an active ES256 signing key is used before it is rotated out. On rotation a fresh
    /// key becomes active and the previous key keeps validating (JWKS overlap) until <see cref="KeyOverlap"/>
    /// elapses. Applies only to the persisted key store (non-Development); Development uses an ephemeral key.
    /// </summary>
    public TimeSpan KeyRotationInterval { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// SEC-0006 D1 — the JWKS overlap window: how long a rotated-out key stays published and validating after it
    /// stops signing. Must exceed the maximum lifetime of any token it signed so in-flight tokens keep verifying.
    /// </summary>
    public TimeSpan KeyOverlap { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// SEC-0006 D1 / SEC-0003-style — acknowledge running a production/staging Authorization Server on an
    /// ephemeral (non-persisted) signing key. The boot guard fails closed otherwise (a restart would invalidate
    /// every issued token and the JWKS would be unstable). Leave false for any real deployment.
    /// </summary>
    public bool AllowEphemeralKeyOutsideDevelopment { get; set; }
}
