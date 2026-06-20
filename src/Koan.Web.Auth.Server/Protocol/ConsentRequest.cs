using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D7 — a pending authorization awaiting the user's consent. The row id is the high-entropy, unguessable,
/// single-use <c>rid</c> the app's consent page is handed. It is bound to the initiating browser session (the
/// <see cref="BrowserBinding"/> secret, echoed in an httpOnly cookie) AND to the exact request tuple, and
/// fast-expires — so approval can only come from the same browser for the same request.
/// </summary>
public sealed class ConsentRequest : Entity<ConsentRequest>
{
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public string CodeChallengeMethod { get; set; } = Pkce.MethodS256;
    public string Scope { get; set; } = "";
    public string Resource { get; set; } = "";
    public string? State { get; set; }
    public string? Nonce { get; set; }

    /// <summary>A secret tying this request to the browser that started it (echoed in an httpOnly cookie).</summary>
    public string BrowserBinding { get; set; } = "";

    /// <summary>pending | approved | denied — set once, then the rid is spent.</summary>
    public string Status { get; set; } = StatusPending;

    public DateTimeOffset ExpiresUtc { get; set; }

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusDenied = "denied";

    public bool IsPending => Status == StatusPending;
    public bool IsExpired(DateTimeOffset now) => ExpiresUtc <= now;
}
