namespace Koan.Web.Extensions.Authorization;

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
}
