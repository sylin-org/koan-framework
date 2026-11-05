namespace Koan.Web.Authorization;

public enum AuthorizeDefault
{
    Allow,
    Forbid,
}

/// <summary>
/// SEC-0002 — the seam-level fallback applied when every provider defers (generalizes WEB-0047's
/// <c>DefaultBehavior</c>). Defaults to <see cref="AuthorizeDefault.Allow"/> to preserve the historical
/// allow-by-default posture.
/// </summary>
public sealed class AuthorizeOptions
{
    public const string SectionPath = "Koan:Web:Authorization";

    public AuthorizeDefault DefaultDecision { get; init; } = AuthorizeDefault.Allow;

    /// <summary>
    /// ARCH-0146 — whether the host stops at start over a routed action of the application's that declares neither a gate nor
    /// <c>[Open("why")]</c>. False, the default, starts it as it is.
    /// </summary>
    public bool RoutesDeclareAccess { get; set; }
}
