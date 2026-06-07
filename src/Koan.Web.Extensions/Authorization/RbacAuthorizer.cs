using System.Security.Claims;
using Koan.Web.Hooks;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// SEC-0001 §8 Tier-0 — the in-process RBAC floor (no external dependency). Reads coarse roles straight
/// from the principal: an action with no required roles is allowed; otherwise the principal must hold one
/// of them. An unauthenticated caller facing a requirement is <em>challenged</em> (401) rather than
/// <em>forbidden</em> (403), so a token/cookie can still be presented.
/// </summary>
public sealed class RbacAuthorizer : IAuthorize
{
    public AuthorizeDecision Authorize(
        ClaimsPrincipal principal,
        string action,
        IReadOnlyCollection<string>? requiredRoles = null,
        string? resource = null)
    {
        if (requiredRoles is null || requiredRoles.Count == 0)
            return AuthorizeDecision.Allowed();

        if (principal.Identity?.IsAuthenticated != true)
            return AuthorizeDecision.Challenged();

        var has = requiredRoles.Any(required =>
            principal.FindAll(ClaimTypes.Role).Any(c => string.Equals(c.Value, required, StringComparison.Ordinal)));

        return has
            ? AuthorizeDecision.Allowed()
            : AuthorizeDecision.Forbidden($"requires one of: {string.Join(", ", requiredRoles)}");
    }
}
