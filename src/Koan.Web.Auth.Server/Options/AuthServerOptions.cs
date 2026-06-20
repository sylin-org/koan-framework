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
}
