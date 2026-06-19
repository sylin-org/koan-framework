# SEC-0005: Governed agent access — grants, audit, the Door

**Status**: **Accepted (2026-06-19)** — the realization of **ARCH-0092 Phase 4 / P3.1** ("governed, revocable, audited agent access"), re-derived onto the [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md) gate·constrain·project model (the original P3.1 card predates SEC-0004 and is stale where it conflicts; see *Rejected*).
**Date**: 2026-06-19
**Deciders**: Enterprise Architect
**Scope**: How an agent's access is *granted* (beyond its token), *audited*, and *disclosed* — the governed-access layer over the SEC-0004 gate. Cross-surface (REST + MCP) because the gate is.
**Related**: [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md) (the gate this binds to) · [ARCH-0092](ARCH-0092-entity-exposure-surfaces.md) §D / P3.1 · [SEC-0001](SEC-0001-fleet-identity-and-trust-fabric.md) (`Identity.Current`, subject id) · the agent-native **Wall / Door / Verb** model (docs/assessment/09 §8).

---

## Context

SEC-0004 made the gate compute, per request, **`Needs ≤ grant`**: the `[Access]` declaration is the *Needs*, the caller's `ClaimsPrincipal` claims are the *grant*, and the projection advertises exactly what the caller may invoke. Three things are still missing for *governed agent access* — and the 09 §8 review confirms only two are genuinely net-new primitives; everything else **binds to the existing gate**:

1. **Grants beyond the token.** An agent's authority should be issuable server-side — *"kitchen-agent may mutate PantryItem for the next 8 hours"* — **queryable, revocable, expiring**, distinct from whatever scopes its token carries. The token is what the agent *is*; a grant is what it has been *lent*.
2. **Audit.** A mutating agent call against a sensitive entity should leave a queryable trail — through the normal entity path, not a bespoke log.
3. **The Door.** The projection is currently binary (Verb visible / Wall absent). The 09 §8 model adds a third state — a **Door**: a walled verb whose `Needs` exceeds the grant but is *signposted* ("you could do X with grant Y"), drift-proof because the signpost derives from the **same `Needs` that enforces it** (Invariant: *Description = Enforcement*). Default stays **Wall**; admin tiers are **always Walls**.

## Rejected (the stale P3.1 card)

The P3.1 card (docs/assessment/prompts/07/P3.1-governed-agent-access.md) was written before SEC-0004. Its core "DECIDED" points are **rejected**:

- **`[McpEntity(Expose = McpAccess.Read | Mutate)]`** — read-only-by-default + access config back *on* `[McpEntity]`. ARCH-0092 Phase 3.3b **removed** access config from `[McpEntity]` (it is pure exposure; access is the `[Access]` gate). Re-adding it re-forks the per-surface authz the gate just unified. The gate stays the single access authority.
- **`McpAccess.Read | Mutate`** grain — predates the gate's `read`/`write`/`remove` actions.
- **`S16.PantryPal` dogfood** — the sample was retired.

What survives: grants are entities; audit is an entity; revocation rides the cache pillar; agent identity is the threaded principal. Those are kept.

## Decision

Three additions, each binding to the SEC-0004 gate. Nothing per-transport — the gate is the one choke point REST and MCP share.

### 1. `AgentGrant` — server-side grants on the gate

`AgentGrant : Entity<AgentGrant>` (the Koan move — grants are queryable/revocable/observable like any entity):

```csharp
public sealed class AgentGrant : Entity<AgentGrant>
{
    public string Subject { get; set; } = "";     // the agent's subject id (sub / NameIdentifier)
    public string Capability { get; set; } = "";   // an [Access] term: "is:admin" / "has:scope:orders:fulfill"
    public string Resource { get; set; } = "*";    // entity name, or "*" for any
    public DateTimeOffset? ExpiresAt { get; set; }  // null = no expiry
}
```

**Materialization (the integration):** the `EntityFloorAuthorizationProvider` evaluates the gate against the token principal first. Only when the gate **denies** (the common path is unaffected — an Allow/open action never loads grants) does it load the subject's *active* grants for that resource, **materialize them as scoped effective-claims** (a `Capability` of `has:scope:x` → a `scope` claim; `is:admin` → a role claim), and re-evaluate the **same** gate against the enriched principal. So a grant composes with the gate's bags, with `origin`, and with `Constrain` for free — the row-narrowing `Constrain` still applies (a grant satisfies the coarse *Needs*, never the row filter). An anonymous caller has no subject id → no grants (you grant to a known agent).

**Revocation:** grants are loaded fresh per request (memoized per request, not across) — `Remove()` or expiry takes effect on the next call, fleet-wide, with **zero new machinery** (no epoch system; the cache pillar's invalidation is a later perf optimization, not a correctness dependency).

### 2. `AgentAction` — mutation audit

`AgentAction : Entity<AgentAction> { Subject, Resource, Action, EntityId, At }`. An entity opts in with **`[Audit]`**; every **mutating** call (write/remove) to an audited entity writes one `AgentAction` row through the normal entity path (queryable/streamable like everything else). **Reads are never audited** (volume). Written after the gate allows and the mutation succeeds.

### 3. The Door — disclosed walls

An entity opts into disclosure with **`[Door]`**. For that entity, a verb the caller may **not** invoke (the gate denies) is projected as a **door** — listed with `door: { needs: "<the unsatisfied [Access] terms>" }` (named + how-to-unlock) — instead of vanishing. Without `[Door]` the verb is a **Wall** (absent — the default, so admin/privileged tiers are silent by construction: *never default a capability to Door*). The signpost's `needs` is rendered from the same `AccessGate` the gate enforces, so it cannot drift from enforcement. The door is **disclosure only** — the verb is still denied on call.

## Slices

1. **AgentGrant core** — the entity + floor materialization (token-denied slow path) + per-request fresh revocation. Integration tests through `AddKoan()`.
2. **Audit** — `AgentAction` + `[Audit]` + write-once-per-mutation on the endpoint write/delete paths.
3. **Door** — `[Door]` + the projection signpost state in `tools/list` + the `koan://entities` catalog.

## Consequences

- **Backward-compatible:** no `AgentGrant`/`[Audit]`/`[Door]` declared = byte-identical to SEC-0004 today (grants load only on a denial, audit/door are opt-in).
- **One authority:** grants enrich the *gate* decision, so REST and MCP grant identically — no per-transport grant path.
- **Deferred:** per-verb (vs per-entity) Door granularity; cache-backed grant lookup (perf); `owner` as a grantable capability (ownership is row-bound `Constrain`, not a claim — a grant expresses `is`/`has` terms only).
