using System;
using System.Reflection;
using System.Security.Claims;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;

namespace Koan.Mcp.Execution;

/// <summary>
/// SEC-0004 Phase 3.3b — the MCP coarse-gate probe. The single authority for an MCP entity tool is now the
/// data-layer <see cref="AccessGate"/> (enforced inside <c>CallToolFor</c> → the endpoint service), so the
/// transport edge no longer carries its own scope check. This probe lets the catalog / <c>tools/list</c> ASK
/// the SAME gate — via the shared singleton <see cref="IAccessGateCache"/> the floor provider consumes — whether
/// a verb is even coarsely reachable, so visibility never advertises what the gate will then deny (the
/// visibility≠enforcement divergence the AN3/WEB-0068 lesson warns against). It replaces the entity-tool half of
/// <see cref="McpToolAccessPolicy"/> (which now governs custom <c>[McpTool]</c> verbs only).
///
/// This is COARSE (no row): an <c>owner</c> term degrades to <c>authenticated</c> exactly as the floor provider
/// and the Slice C projection's coarse seam do — the per-row refinement happens at the data layer on the actual
/// call. A <c>null</c> principal is STDIO local-trust (the raw handler is unfiltered by design; visibility
/// filtering is a remote-edge concern), so it returns <c>true</c>.
/// </summary>
public static class McpEntityGate
{
    /// <summary>Does the entity's compiled gate coarsely allow <paramref name="operation"/> for
    /// <paramref name="user"/>? A <c>null</c> principal (STDIO local-trust) is always allowed.</summary>
    public static bool CoarseAllows(IAccessGateCache gateCache, Type entityType, EntityEndpointOperationKind operation, ClaimsPrincipal? user)
    {
        if (gateCache is null) throw new ArgumentNullException(nameof(gateCache));
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));

        // STDIO local-trust: the raw handler binds with no principal and is unfiltered by design (stdin/stdout
        // is the same-machine process owner). Visibility filtering only applies to a concrete remote caller.
        if (user is null) return true;

        var gate = gateCache.GetOrCompile(entityType);
        var decision = AccessGateEvaluator.Evaluate(
            gate.For(ActionFor(operation)),
            user,
            // Coarse degrade: owner→authenticated (no row to bind here), identical to EntityFloorAuthorizationProvider.
            ownerSatisfied: user.Identity?.IsAuthenticated == true);
        return decision is AuthorizeDecision.Allow;
    }

    /// <summary>
    /// SEC-0005 (the Door) — the door signpost for a verb the caller cannot invoke, or <c>null</c> when the verb is
    /// a Verb (allowed) or a silent Wall. A door is returned only when the entity is <c>[Door]</c>, the caller is
    /// DENIED the action, and the gate is NOT role-gated (a role gate is a privilege tier and stays a Wall — 09 §8).
    /// The returned string is the gate's <c>needs</c> ("requires scope:x or owner"), derived from the SAME gate that
    /// enforces, so it cannot drift. A null principal (STDIO local-trust) has no doors — everything is a Verb.
    /// </summary>
    public static string? DoorNeeds(IAccessGateCache gateCache, Type entityType, EntityEndpointOperationKind operation, ClaimsPrincipal? user)
    {
        if (gateCache is null) throw new ArgumentNullException(nameof(gateCache));
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        if (user is null) return null;                                                  // local-trust: no doors
        if (entityType.GetCustomAttribute<DoorAttribute>(inherit: true) is null) return null; // no [Door] → Wall

        var actionGate = gateCache.GetOrCompile(entityType).For(ActionFor(operation));
        if (actionGate.IsOpen) return null;                                             // open → Verb
        var decision = AccessGateEvaluator.Evaluate(actionGate, user, ownerSatisfied: user.Identity?.IsAuthenticated == true);
        if (decision is AuthorizeDecision.Allow) return null;                           // the caller may invoke → Verb
        if (AccessGateEvaluator.RequiresRole(actionGate)) return null;                  // privilege tier → silent Wall
        return AccessGateEvaluator.Describe(actionGate);                                // a disclosed door + its needs
    }

    /// <summary>Maps a 12-op endpoint operation to the gate's three-verb action — the SAME read/write/remove split
    /// the endpoint service enforces (mutations→write, deletes→remove, everything else→read).</summary>
    public static string ActionFor(EntityEndpointOperationKind operation) => operation switch
    {
        EntityEndpointOperationKind.Upsert
            or EntityEndpointOperationKind.UpsertMany
            or EntityEndpointOperationKind.Patch => EntityAuthorizeActions.Write,
        EntityEndpointOperationKind.Delete
            or EntityEndpointOperationKind.DeleteMany
            or EntityEndpointOperationKind.DeleteByQuery
            or EntityEndpointOperationKind.DeleteAll => EntityAuthorizeActions.Remove,
        // Collection / Query / GetById / GetNew (and any future read-shaped op) → read.
        _ => EntityAuthorizeActions.Read,
    };
}
