using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Koan.Web.Hooks;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§A) — the ONE evaluator for an <see cref="ActionGate"/>. Pure and static (no DI, no allocation on the
/// satisfied path beyond the decision record): the gate provider calls it now with a degraded owner result, and
/// Slice B/C's row-bound projection will call the same logic with a real row. An open gate allows; otherwise the
/// first satisfied bag (OR) allows; if none is satisfied, an unauthenticated principal facing an auth-requiring
/// gate is <see cref="AuthorizeDecision.Challenge"/> (401) and an authenticated one is
/// <see cref="AuthorizeDecision.Forbid"/> (403) — reproducing the legacy floor's challenge-vs-forbid split exactly.
/// </summary>
public static class AccessGateEvaluator
{
    // Mirrors EntityFloorAuthorizationProvider's scope claim scan (kept identical so lowered [RequireScope] is honored).
    private static readonly string[] ScopeClaimTypes =
    {
        "scope",
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope",
    };

    /// <summary>Coarse (no-row) gate evaluation. <paramref name="ownerSatisfied"/> is the degraded owner result —
    /// at the gate the provider passes <c>principal.Identity.IsAuthenticated</c> (an owner term cannot bind a row
    /// here; see SEC-0004 ownerAtGateTime).</summary>
    public static AuthorizeDecision Evaluate(ActionGate gate, ClaimsPrincipal user, bool ownerSatisfied)
    {
        if (gate.IsOpen) return AuthorizeDecision.Allowed();

        var authed = user.Identity?.IsAuthenticated == true;
        foreach (var bag in gate.AnyOf)
        {
            if (BagSatisfied(bag, user, authed, ownerSatisfied)) return AuthorizeDecision.Allowed();
        }

        // No bag satisfied: distinguish "sign in first" from "you may not". An anonymous principal facing a gate
        // whose every bag needs authentication is Challenged; otherwise Forbidden.
        return !authed && AllBagsNeedAuth(gate)
            ? AuthorizeDecision.Challenged()
            : AuthorizeDecision.Forbidden(Describe(gate));
    }

    /// <summary>
    /// SEC-0004 Slice C seam (carried-but-unused in Slice B): row-bound evaluation. <paramref name="ownerProbe"/>
    /// is invoked LAZILY — only when some bag actually requires <c>owner</c> — so the row's Owner predicate is
    /// compiled/run only when it can affect the outcome. Slice C's per-row <c>can:[]</c> projection binds the
    /// realization's single Owner predicate here; the coarse gate provider still degrades owner→authenticated.
    /// </summary>
    public static AuthorizeDecision Evaluate(ActionGate gate, ClaimsPrincipal user, Func<bool> ownerProbe)
    {
        var ownerSatisfied = gate.AnyOf.Any(b => b.RequiresOwner) && ownerProbe();
        return Evaluate(gate, user, ownerSatisfied);
    }

    /// <summary>Is a single bag satisfied? <c>anyone</c> short-circuits; otherwise auth, any-of roles, all-of
    /// grants, and (degraded) ownership must all hold.</summary>
    public static bool BagSatisfied(AccessBag bag, ClaimsPrincipal user, bool authed, bool ownerSatisfied)
    {
        if (bag.Anyone) return true;
        // Defense-in-depth: a bag with NO positive condition must never silently allow. The string parser can
        // never produce one (every non-`anyone` term sets Authenticated=true), but a hand-built or fluent-builder
        // bag could — deny it rather than fall through the all-skip path to an accidental allow.
        if (!bag.Authenticated && !bag.RequiresOwner && bag.IsRolesAnyOf.Count == 0 && bag.HasAllOf.Count == 0)
        {
            return false;
        }
        if (bag.Authenticated && !authed) return false;
        if (bag.IsRolesAnyOf.Count > 0 && !bag.IsRolesAnyOf.Any(user.IsInRole)) return false;
        foreach (var grant in bag.HasAllOf)
        {
            if (!Holds(user, grant)) return false;
        }
        if (bag.RequiresOwner && !ownerSatisfied) return false;
        return true;
    }

    private static bool Holds(ClaimsPrincipal user, Grant grant) => grant switch
    {
        Grant.Scope s => HasScope(user, s.Value),
        Grant.Role r => user.IsInRole(r.Value),
        Grant.RoleAnyOf ra => ra.Roles.Any(user.IsInRole),
        Grant.Claim c => user.FindAll(c.Type).Any(cl => string.Equals(cl.Value, c.Value, StringComparison.Ordinal)),
        _ => false,
    };

    private static bool HasScope(ClaimsPrincipal user, string scope)
    {
        foreach (var claimType in ScopeClaimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                if (string.IsNullOrWhiteSpace(claim.Value)) continue;
                foreach (var value in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (string.Equals(value, scope, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        return false;
    }

    private static bool AllBagsNeedAuth(ActionGate gate)
        => gate.AnyOf.All(b => !b.Anyone && (b.Authenticated || b.IsRolesAnyOf.Count > 0 || b.HasAllOf.Count > 0 || b.RequiresOwner));

    /// <summary>SEC-0005 (Door) — a human description of what would satisfy a gate ("requires scope:x or owner"),
    /// the same string the Forbid decision carries. Used as the Door signpost's <c>needs</c>, so it cannot drift
    /// from enforcement.</summary>
    public static string Describe(ActionGate gate)
        => "requires " + string.Join(" or ", gate.AnyOf.Select(DescribeBag));

    /// <summary>SEC-0005 (Door) — true when any path to satisfy the gate requires a ROLE. A role gate is a
    /// PRIVILEGE tier (09 §8: "admin is a Wall, not a Door"), so such verbs stay silent Walls even on a <c>[Door]</c>
    /// entity — disclosing one would leak that a privileged capability exists (privilege enumeration).</summary>
    public static bool RequiresRole(ActionGate gate)
        => gate.AnyOf.Any(b => b.IsRolesAnyOf.Count > 0 || b.HasAllOf.Any(g => g is Grant.Role or Grant.RoleAnyOf));

    private static string DescribeBag(AccessBag bag)
    {
        if (bag.Anyone) return "anyone";
        var parts = new List<string>();
        if (bag.IsRolesAnyOf.Count > 0) parts.Add("role in [" + string.Join(", ", bag.IsRolesAnyOf) + "]");
        foreach (var grant in bag.HasAllOf) parts.Add(DescribeGrant(grant));
        if (bag.RequiresOwner) parts.Add("owner");
        if (parts.Count == 0 && bag.Authenticated) parts.Add("authenticated");
        return parts.Count > 0 ? string.Join(" and ", parts) : "an unsatisfiable condition";
    }

    private static string DescribeGrant(Grant grant) => grant switch
    {
        Grant.Scope s => $"scope:{s.Value}",
        Grant.Role r => $"role:{r.Value}",
        Grant.RoleAnyOf ra => "role in [" + string.Join(", ", ra.Roles) + "]",
        Grant.Claim c => $"claim:{c.Type}={c.Value}",
        _ => grant.ToString() ?? "grant",
    };
}
