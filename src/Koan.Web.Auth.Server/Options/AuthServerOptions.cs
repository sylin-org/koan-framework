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
    /// SEC-0006 — the AS's canonical public origin (e.g. <c>https://app.example.com</c>), used as the discovery
    /// <c>issuer</c> and to build every externally-advertised endpoint URL (authorize/token/device/jwks +
    /// the device <c>verification_uri</c>). When set it is authoritative and the request <c>Host</c> header is
    /// ignored — the correct, host-spoof-proof posture behind a proxy. When unset (Development), URLs derive
    /// from the live request host.
    /// </summary>
    public string? Issuer { get; set; }

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
    /// SEC-0006 D5 — open Dynamic Client Registration (RFC 7591). On by default (the Claude Desktop happy path:
    /// no pre-shared client_id), but every dynamic client is zero-trust: public-only, loopback-only redirects,
    /// rate-limited, TTL-expired. Turn off for a hardened deployment (pre-registered clients only).
    /// </summary>
    public bool AllowDynamicRegistration { get; set; } = true;

    /// <summary>SEC-0006 D5 — how long a dynamically-registered client lives before it is GC'd.</summary>
    public TimeSpan DynamicClientLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>SEC-0006 D5 — registration attempts allowed per source IP per minute.</summary>
    public int RegistrationRateLimitPerMinute { get; set; } = 10;

    /// <summary>SEC-0006 D5 — registration attempts allowed globally per minute (a flood ceiling).</summary>
    public int RegistrationRateLimitGlobalPerMinute { get; set; } = 100;

    /// <summary>SEC-0006 D8 — how long a device authorization (device_code / user_code) is valid.</summary>
    public TimeSpan DeviceCodeLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>SEC-0006 D8 — the minimum device poll interval advertised to the client (seconds).</summary>
    public int DevicePollIntervalSeconds { get; set; } = 5;

    /// <summary>SEC-0006 D8 — user_code verification attempts allowed per source IP per minute (anti-brute-force).</summary>
    public int UserCodeVerificationRateLimitPerMinute { get; set; } = 10;

    /// <summary>
    /// SEC-0006 D9 — issue rotating refresh tokens (default on) so a client stays connected past the short
    /// access-token lifetime without re-popping the browser. Every refresh rotates with reuse-detection and is
    /// backed by a revocable grant.
    /// </summary>
    public bool EnableRefreshTokens { get; set; } = true;

    /// <summary>SEC-0006 D9 — refresh-token / backing-grant lifetime ("authorize once" window).</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// SEC-0006 D9 — remember consent (per user + client + scope set). A re-connect with an unchanged scope set
    /// and a live grant skips the consent page; new scopes or a revoked grant re-prompt.
    /// </summary>
    public bool RememberConsent { get; set; } = true;

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
