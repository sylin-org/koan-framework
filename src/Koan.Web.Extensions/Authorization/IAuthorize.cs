using System.Security.Claims;
using Koan.Web.Hooks;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// SEC-0001 §8 — the resource-side authorization seam. Coarse identity/roles travel in the token; the
/// fine-grained, fast-changing, revocable decision is made HERE, at the resource, against the current
/// principal (cookie or bearer alike), using one decision vocabulary (<see cref="AuthorizeDecision"/>).
/// <para>
/// Phase 2 ships this seam in PARALLEL with the live ASP.NET <c>IAuthorizationService</c> path;
/// <c>CapabilityAuthorizer</c> is flipped to route through it in increment 2k. The Tier-0 implementation
/// is <see cref="RbacAuthorizer"/>; a host can substitute a PDP/ReBAC adapter (SEC-0001 §8 ladder).
/// </para>
/// </summary>
public interface IAuthorize
{
    /// <summary>
    /// Decide whether <paramref name="principal"/> may perform <paramref name="action"/> on the optional
    /// <paramref name="resource"/>. When <paramref name="requiredRoles"/> is null/empty the action is
    /// unrestricted (the permissive Tier-0 floor).
    /// </summary>
    AuthorizeDecision Authorize(
        ClaimsPrincipal principal,
        string action,
        IReadOnlyCollection<string>? requiredRoles = null,
        string? resource = null);
}
