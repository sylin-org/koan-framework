using System;
using System.Security.Claims;
using Koan.Web.Hooks;
using Koan.Web.Authorization;

namespace Koan.Web.Extensions.Authorization;

/// <summary>
/// SEC-0002 §8 Tier-0 — the in-process RBAC floor (no external dependency). Evaluates an explicit role
/// requirement carried on the request: unauthenticated caller → <c>Challenge</c>; holds a required role →
/// <c>Allow</c>; otherwise → <c>Forbid</c>. Defers (<c>null</c>) when the request carries no role requirement,
/// so higher rungs (named policies, PDP/ReBAC) can decide.
/// </summary>
public sealed class RbacAuthorizationProvider : IAuthorizationProvider
{
    public int Order => 0;

    public Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        var required = request.RequiredRoles;
        if (required is null || required.Count == 0)
            return Task.FromResult<AuthorizeDecision?>(null); // no role requirement → defer

        if (request.Subject.Identity?.IsAuthenticated != true)
            return Task.FromResult<AuthorizeDecision?>(AuthorizeDecision.Challenged());

        var has = required.Any(role =>
            request.Subject.FindAll(ClaimTypes.Role).Any(c => string.Equals(c.Value, role, StringComparison.Ordinal)));

        return Task.FromResult<AuthorizeDecision?>(has
            ? AuthorizeDecision.Allowed()
            : AuthorizeDecision.Forbidden($"requires one of: {string.Join(", ", required)}"));
    }
}
