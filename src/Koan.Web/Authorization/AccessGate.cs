using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Claims;
using Koan.Web.Hooks;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§A) — the authority for one action: an OR-list of <see cref="AccessBag"/> bags (disjunctive normal
/// form). <see cref="Open"/> (the empty list) is the allow-by-default case — an unspecified action is open.
/// </summary>
public sealed record ActionGate(IReadOnlyList<AccessBag> AnyOf)
{
    /// <summary>The open action — no bags, so the gate defers (allow-by-default).</summary>
    public static readonly ActionGate Open = new(Array.Empty<AccessBag>());

    /// <summary>True when no bag is declared — the action is open and the provider defers.</summary>
    public bool IsOpen => AnyOf.Count == 0;

    /// <summary>
    /// Coarse (no-row) evaluation: ergonomic delegate to <see cref="AccessGateEvaluator.Evaluate(ActionGate, System.Security.Claims.ClaimsPrincipal, bool)"/>. At the gate an
    /// <c>owner</c> term degrades to <paramref name="ownerSatisfied"/> (the caller passes
    /// <c>principal.Identity.IsAuthenticated</c>). Slice B adds a row-bound overload that computes ownership from a
    /// loaded row, reusing the same bag logic.
    /// </summary>
    public AuthorizeDecision Evaluate(ClaimsPrincipal principal, bool ownerSatisfied = false)
        => AccessGateEvaluator.Evaluate(this, principal, ownerSatisfied);
}

/// <summary>
/// SEC-0004 (§A) — the whole-entity gate: a map from action verb (read/write/remove and, later, custom verbs) to
/// the <see cref="ActionGate"/> for that action. Immutable; produced once by <c>AccessGateCache</c> from the
/// <c>[Access]</c> attribute, lowered legacy floor attributes, and (Slice B) an <c>EntityAccess&lt;T&gt;</c>
/// realization, then evaluated per request.
/// </summary>
public sealed record AccessGate(
    IReadOnlyDictionary<string, ActionGate> ByAction,
    IReadOnlyDictionary<string, ActionGate> Custom)
{
    /// <summary>Every action open — the no-declaration entity (allow-by-default). Read-only backing so the shared
    /// singleton cannot be mutated via a downcast.</summary>
    public static readonly AccessGate Open = new(
        new ReadOnlyDictionary<string, ActionGate>(new Dictionary<string, ActionGate>(StringComparer.OrdinalIgnoreCase)),
        new ReadOnlyDictionary<string, ActionGate>(new Dictionary<string, ActionGate>(StringComparer.OrdinalIgnoreCase)));

    /// <summary>The gate for <paramref name="action"/> — a declared per-action gate, then a custom verb gate, else
    /// <see cref="ActionGate.Open"/>. <c>Custom</c> is empty in Slice A; custom projection verbs register there in
    /// later slices with zero schema churn.</summary>
    public ActionGate For(string action)
        => ByAction.TryGetValue(action, out var g) ? g
         : Custom.TryGetValue(action, out var c) ? c
         : ActionGate.Open;
}
