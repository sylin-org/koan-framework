using Koan.Web.Hooks;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// SEC-0002 — the single resource-side authorization decision seam. Channel-agnostic by contract: it takes a
/// principal, an action, and an optional resource (no <c>HttpContext</c>), and returns one
/// <see cref="AuthorizeDecision"/>, so HTTP, the message bus, and jobs authorize through the same call. The
/// decision is produced by the capability-graded <see cref="IAuthorizationProvider"/> ladder
/// (RBAC floor → named-policy → PDP/ReBAC).
/// </summary>
public interface IAuthorize
{
    Task<AuthorizeDecision> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default);
}
