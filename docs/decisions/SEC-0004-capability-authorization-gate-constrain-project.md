# SEC-0004: Koan capability authorization — gate, constrain, project

**Status**: **Accepted (2026-06-19)** — direction signed off by the Enterprise Architect after a research pass across the loved authorization systems (Laravel/Pundit/CanCanCan, Firebase/Supabase-RLS/PocketBase/Convex/Appwrite, CASL/Spring/Clerk/tRPC, Zanzibar/OpenFGA/SpiceDB/Oso/OPA) and capability-vs-RBAC theory. Realized as the **capstone of ARCH-0092 Phase 3** (the entity-exposure consolidation). Supersedes the entity-wide floor shipped in ARCH-0092 Phases 3.1/3.2 by *absorbing* it (the seam stays; the coarse floor becomes the gate; WEB-0068 becomes `Constrain`'s rail; the `Koan-Access-*` headers become the projection's single-item form).
**Date**: 2026-06-19
**Deciders**: Enterprise Architect
**Scope**: The developer-facing **capability authorization model** for entity surfaces (REST + MCP, and any future surface over `IEntityEndpointService`). Defines *how a developer declares who may do what to which rows*, and how the framework *advertises* the resulting authority back to clients/agents.
**Related**: [SEC-0002](SEC-0002-unified-authorization-model.md) (the `IAuthorize` seam — the evaluation engine this model declares *into*; this ADR resolves SEC-0002's deferred "step 5 / physical hoist" — the dormant `IAuthorizeHook` was deleted, not folded, and the seam was hoisted to `Koan.Web`) · [ARCH-0092](ARCH-0092-entity-exposure-surfaces.md) §D (the access floor — this ADR is its realization) · [WEB-0068](WEB-0068-query-options-predicates.md) (read-path visibility predicates — generalized here to all verbs as `Constrain`) · **P3.1** (governed agent access — `AgentGrant` is a relationship a gate/constraint resolves against; it lands on this model).

---

## Context

ARCH-0092 §D unified authorization onto one seam (`IAuthorize`) called at one point in the shared `IEntityEndpointService` — so REST and MCP gate identically (the single most-cited correctness property in the research: *the same entity must not grant different access depending on which door you came in*). Phases 3.1/3.2 shipped that, with a coarse **entity-wide floor** (`[Authorize]`/`[AllowAnonymous]`/`[RequireScope]`).

The entity-wide floor cannot express the most common shapes — *"public read / authenticated write"*, *"admin-only delete"*, and especially *"edit **this** row because you own it"*. A research pass across the systems developers actually love converged on five findings that reshape the design:

1. **There is one front-door verb: `can(subject, action, resource)`.** Roles/claims/scopes/ownership are the *implementation beneath it*, never a co-equal "does the user have role X?" verb. Every system that put "has role" at the front door (Django built-in, NextAuth, role-first Spring) became the cautionary tale; every loved one (Laravel `$user->can('update',$post)`, Pundit, CanCanCan, Oso `authorize`, Zanzibar/OpenFGA/SpiceDB `Check`) leads with resource-aware "can." So "has" is the *authoring vocabulary*, not a second mental model.
2. **The rule that authorizes one row should *be* the filter that scopes the collection.** CanCanCan's `accessible_by` and Postgres RLS are the two most-praised mechanisms; Firebase's #1 footgun is *"rules are not filters"* (forcing developers to hand-re-encode the predicate as a query that silently drifts). A **query transform** makes "the rule is the filter" true by construction — and bounds mass-delete/mass-query for free (the agent-safety property).
3. **Ownership is a relationship/predicate over the row, not a claim.** "Can edit *this* post" must bind authority to the specific object (capability theory: this is what defeats the confused-deputy / ambient-authority / role-explosion traps). `editor-of-project-X` as a *role* is the named failure.
4. **Per-operation is the right grain, but declare-once is the missing piece.** RLS (4 policies), PocketBase (5 fields) all validate read-scope ≠ write-scope — and all force hand-repeating the predicate. `create` is special everywhere: no row to filter, so the rule must **stamp/validate the incoming payload's owner** (the universal privilege-escalation footgun — forged owner id, missing `WITH CHECK`).
5. **Stringly-typed encodings are the universal wound** (Spring SpEL, CASL conditions, Clerk `org:x:y`, Django codenames — all fail at runtime). A typed framework should make the row layer *code* with refactor-safe selectors.

One deliberate divergence from the research: it says **secure-by-default** (deny-by-omission) is the breach dividing line. Koan **chooses allow-by-default** for developer delight (mark an entity `[RestEntity]` and it works — no auth ceremony to get started), and pays the honesty cost a different way (see §C, the projection): the framework *tells you* your authority, so "open" is never silently assumed.

---

## Decision

A three-layer model — **gate · constrain · project** — declared with one vocabulary (arrays of tokens), composable from "nothing" up.

### A. Gate — *who may touch this entity at all, per action* (coarse, identity-only)

A per-action tag-bag, evaluated before any row is touched (cheap; no data load):

- `is: [...]` — the principal's **role** is any of these (OR). Sugar for `IsInRole`.
- `has: [...]` — the principal holds **all** of these typed grants: `scope:x`, `role:y`, `claim:z=v` (AND).
- `owner` — the principal satisfies the entity's `Owner` predicate (resource-aware; see §B — only meaningful where a row exists).
- Within one bag, `is` **AND** `has`; a **list of bags = OR** → disjunctive normal form, boolean-complete for the monotone cases. `anyone` / `authenticated` are the open / signed-in tokens.

**Terse** (RBAC only): `[Access(read: "anyone", write: "is:member", remove: "is:admin")]` on the entity.
**Realization** (when you also need rows): `AccessGate` properties on `EntityAccess<T>`, fluent for OR-of-ANDs: `Gate.Is("admin").Or(Gate.Owner)`.

**Allow-by-default, per-action:** an unspecified action bucket is **open**. You gate exactly what you name — locking writes leaves reads open unless you say otherwise.

### B. Constrain — *which rows, per action* (fine-grained, resource-aware, the query transform)

An overridable method on the realization class (controller/toolset/`EntityAccess<T>`) that **transforms the query**, so the same declaration filters the collection, 404s an out-of-scope single fetch, and bounds a mass operation — one mechanism, both surfaces, DB-pushable (WEB-0068 is the rail):

```csharp
public override IAccessFilter<Order> Constrain(IAccessFilter<Order> q, AccessAction action)
    => action == AccessAction.Create
        ? q.Stamp(o => o.CustomerId, CurrentUserId)   // create has no row → stamp/validate the payload
        : q.Where(Owner);                             // read/update/delete → narrow to own rows
```

Default `Constrain` is a no-op (returns the query unchanged → all rows, consistent with allow-by-default). **Ownership is declared once** (`protected override Expr<Order> Owner => o => o.CustomerId == CurrentUserId;`) and consumed by *both* the gate (`Gate.Owner`) and `Constrain` — the single source of truth the research demands. Selectors are typed (`o => o.CustomerId`), not strings — the row layer is code.

### C. Project — *what you may actually do, per item* (the honesty counterweight)

Every response advertises the principal's **realized** authority as an open-vocabulary `can` array — the exhaustive set of permitted verbs (absence = denied), **including custom verbs**, not just CRUD:

- **Single item** → header `Koan-Access: read, write` (collapses the three booleans; lists the permitted verbs).
- **Collection** → opt-in sidecar that keeps the entity body clean:
  ```json
  { "items": [ … ], "access": { "ord-123": { "can": ["read","write"] },
                                 "ord-456": { "can": ["read"] } } }
  ```
- **MCP** → per-item `can: [...]` in the tool-result metadata, **default-on** (agents need it to plan; MCP carries structured metadata natively).

The per-row `can` is **free**: it is the gate result for each verb `AND` whether the row satisfies that verb's `Constrain` predicate (evaluated in-memory against the already-fetched rows). One declaration → query filter **and** per-row capability manifest. A custom toolset/controller verb (e.g. `Fulfill`) declares its own gate (+ optional `Constrain`) and appears in `can` exactly when permitted — zero extra wiring. **This is what makes allow-by-default honest:** the projection states authority, so "open" is advertised, never silently assumed — the agent-native master law (description = enforcement) applied in the positive direction.

### D. Composability — nothing → attribute → override

Mirrors the `[RestEntity]` ↔ `EntityController` terse/realization symmetry:

| You write | You get |
|---|---|
| nothing | open (allow-by-default) — "just works" |
| `[Access(...)]` on the entity | coarse per-action RBAC gate |
| override `Constrain` (+ `Owner`/`AccessGate`) on the controller/toolset | fine-grained, resource-aware rows + custom-verb gates |

Each layer is additive; you reach for the next only when you need it.

---

## Consequences

**Positive**
- One verb (`Can`) at the front; "has role/scope/claim" demoted to the *authoring* vocabulary — the research's unanimous shape, so the model scales without role-explosion / confused-deputy / stale-token traps.
- "The rule *is* the filter" — collection, single-item, and mass-operation authority all derive from one `Constrain`, DB-pushed, no re-encoding, no N+1, and **agent-safe** (a `delete-by-query` cannot exceed the principal's rows).
- Per-row capability projection is a **novel** legibility win (no researched system advertises per-row `can`) and the honesty counterweight that earns the open default.
- Open-vocabulary `can` manifest covers domain verbs, not just CRUD — agents get a complete plan surface.
- Absorbs prior work: SEC-0002 seam (engine), ARCH-0092 §D (floor → gate), WEB-0068 (→ `Constrain`), the `Koan-Access-*` headers (→ projection). Net concept count goes *down*.

**Costs / negative**
- A Koan-specific authoring surface (the gate grammar + `EntityAccess<T>`) beyond standard `[Authorize]` — accepted: standard attributes are binary and can't express resource-aware per-verb rules; the research shows the loved systems all minted their own surface for exactly this.
- Allow-by-default means an un-gated mutation is open. Mitigated by the projection (honest) and by the fact that gating is one attribute away; **not** mitigated by a wall. A future opt-in `secure-by-default` switch is left open (Deferred).
- Per-row projection costs N predicate evaluations per collection response (in-memory, cheap; opt-in for REST collections).

**Risks**
- `create`-stamping is the subtle correctness point (a `Where` on create must become a *stamp/validate* of the incoming payload, or it's a no-op that lets a forged owner through). Pinned by a conformance test.
- The gate string-DSL in the `[Access]` attribute is the one stringly-typed surface; validated at registration (fail-fast on an unknown token), and the row layer (`Constrain`) stays typed.

---

## Implementation (phased — realizes ARCH-0092 Phase 3 capstone)

1. **Gate** — `AccessGate` bag + `[Access(...)]` attribute + the DNF grammar + a gate provider on the seam (supersedes the entity-wide `[Authorize]`/`[RequireScope]` floor; those remain recognized as gate sugar). Per-action; allow-by-default.
2. **Constrain** — `EntityAccess<T>` realization base + `Owner` once + `Constrain(query, action)` wired through WEB-0068 for reads and through the write/delete paths in `IEntityEndpointService`; `create` stamping.
3. **Project** — the `can` manifest: `Koan-Access` list header (single), opt-in `access` sidecar (collection), default-on MCP metadata. Per-row from gate ∩ `Constrain`.
4. **MCP edge** (ARCH-0092 Phase 3.3) re-pointed at this model; `[McpEntity(RequiredScopes/RequireAuthentication)]` → gate; demote `[McpEntity]`; dogfood S16.PantryPal.
5. **P3.1** grants land on the gate (`AgentGrant` = a relationship `is`/`has`/`owner` resolves against), toolset-grained.

## Explicitly deferred / out of scope

- **Secure-by-default opt-in** — a per-app/per-entity switch flipping the default closed; deliberately OFF for delight, revisited if real deployments want a wall.
- **Field-level authorization** — `can` is per-row, per-verb; per-field read/write masking is a later decision.
- **External PDP/ReBAC providers** (OpenFGA/SpiceDB/Oso adapters) — the gate/`Constrain` model is the in-process realization; a relationship-engine rung on the seam (SEC-0002 Tiers 2+) stays future, opt-in.
- **Core-ward hoist of the seam** (for jobs/bus) — ARCH-0092 landed it in `Koan.Web`; deeper hoist deferred to a real consumer.
