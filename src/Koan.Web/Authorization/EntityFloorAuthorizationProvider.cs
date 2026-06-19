using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Koan.Web.Hooks;

namespace Koan.Web.Authorization;

/// <summary>
/// ARCH-0092 (§D) — the built-in floor rung. Evaluates an entity's declarative access floor
/// (<c>[AllowAnonymous]</c> / <c>[Authorize]</c> / <see cref="RequireScopeAttribute"/>) reflected off the
/// resource entity <see cref="Type"/>, so the same declaration is enforced on every surface (REST + MCP)
/// through the one seam. Registered by default in <c>Koan.Web</c>; richer rungs (named-policy, RBAC, PDP/ReBAC)
/// stack above via <see cref="IAuthorizationProvider"/>.
/// </summary>
/// <remarks>
/// v1 grain: the floor decides authentication, <c>[Authorize(Roles=…)]</c> roles, and scope — entity-wide
/// (it returns <c>null</c>/defers when no floor attribute is present, preserving allow-by-default). Two
/// boundaries are deferred (ADR): (1) finer per-operation scope, and (2) <c>[Authorize(Policy=…)]</c> named
/// policies on an entity — the floor still enforces the authentication an <c>[Authorize]</c> implies, but the
/// named-policy evaluation itself is left to a future policy rung; it never silently allows.
/// </remarks>
public sealed class EntityFloorAuthorizationProvider : IAuthorizationProvider
{
    private static readonly string[] ScopeClaimTypes =
    {
        "scope",
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope"
    };

    /// <summary>After the RBAC floor (0), before named-policy (100): the entity floor is authoritative for
    /// the requirements it understands (authn / roles / scope), deferring policy/PDP to higher rungs.</summary>
    public int Order => 50;

    public Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
        => Task.FromResult(Evaluate(request));

    private static AuthorizeDecision? Evaluate(AuthorizeRequest request)
    {
        if (request.Resource is not Type entityType)
        {
            return null; // the floor only understands entity-typed resources
        }

        // [AllowAnonymous] on the entity opens it outright (mirrors ASP.NET precedence).
        if (entityType.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null)
        {
            return AuthorizeDecision.Allowed();
        }

        var authorize = entityType.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();
        var requireScopes = entityType.GetCustomAttributes<RequireScopeAttribute>(inherit: true).ToArray();

        if (authorize.Length == 0 && requireScopes.Length == 0)
        {
            return null; // no floor declared → defer (ultimately allow-by-default)
        }

        var user = request.Subject;
        if (user?.Identity?.IsAuthenticated != true)
        {
            // Any [Authorize]/[RequireScope] implies authentication. Challenge (401) rather than Forbid (403).
            return AuthorizeDecision.Challenged();
        }

        // [Authorize(Roles=…)] — every declared attribute's role set must be satisfied (at least one role each).
        foreach (var attr in authorize)
        {
            if (string.IsNullOrWhiteSpace(attr.Roles)) continue;
            var roles = attr.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roles.Length > 0 && !roles.Any(user.IsInRole))
            {
                return AuthorizeDecision.Forbidden($"requires role: {attr.Roles}");
            }
        }

        // [RequireScope] — all required scopes (AND across every attribute) must be present.
        var required = requireScopes
            .SelectMany(a => a.Scopes)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (required.Length > 0 && !HasAllScopes(user, required))
        {
            return AuthorizeDecision.Forbidden($"requires scope(s): {string.Join(" ", required)}");
        }

        return AuthorizeDecision.Allowed();
    }

    private static bool HasAllScopes(ClaimsPrincipal user, IReadOnlyList<string> required)
    {
        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claimType in ScopeClaimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                if (string.IsNullOrWhiteSpace(claim.Value)) continue;
                foreach (var value in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    held.Add(value);
                }
            }
        }

        return held.Count > 0 && required.All(held.Contains);
    }
}
