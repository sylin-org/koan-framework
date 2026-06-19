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

> **Status (dev):** Phase 1 (Gate) shipped as `711251b8`, Phase 2 (Constrain) as `83783b75`, Phase 3 (Project) as `1145b2e3` — the `[Access]` per-action gate (legacy `[Authorize]`/`[AllowAnonymous]`/`[RequireScope]` lowered as sugar, shipped floor tests unchanged); `EntityAccess<T>` with `Owner` declared once + `Constrain` riding the WEB-0068 read rail + create-stamp / update-freeze / out-of-scope-404 / bounded mass-delete writes; and the per-row `can:[]` manifest — `RowProjection<T>` (coarse seam ∩ row-bound gate ∩ Constrain, custom verbs gate-only), the row-refined `Koan-Access` single-item header, the opt-in REST `{ items, access }` sidecar (`?access=true`), and the default-on MCP tool-result metadata. All three TDD'd, mutation-checked, and adversarially reviewed (96 tests across Web + MCP conformance; the WEB-0068 and MCP regression suites stay byte-identical).
> **Phase 4 (MCP edge → gate) is underway:** Phase 3.3a shipped as `0c34d910` — the MCP caller's `ClaimsPrincipal` now threads into `EntityRequestContext.User` on both data paths (tools via `CallToolFor`; code-mode via `CodeExecutionRequest.Principal` + a scoped `McpCallContext`), so the unified gate enforces on MCP exactly as on REST. **STDIO defaults to anonymous** (tool *names* stay discoverable over the trusted channel; gated *data* requires an identity — transport-trust ≠ authorization). Phase 3.3b shipped as `4221d6e8` — an entity tool's single authority is now the data-layer `[Access]` gate: `McpToolAccessPolicy.IsEntityToolPermitted` and the `HttpSseRpcBridge` entity-tool pre-check are retired (a denial rides back as `meta.shortCircuit`, the MCP mirror of REST 403/401), and `tools/list` + the `koan://entities`/`koan://self` catalog now ADVERTISE against the *same* gate via the new shared `McpEntityGate.CoarseAllows` (over the singleton `IAccessGateCache` the floor provider consumes, so visibility never disagrees with enforcement). `[McpEntity]` is demoted to pure exposure (`RequiredScopes`/`RequireAuthentication` dropped from the attribute, registration, tool definition, override, descriptor mapper, registry, RPC metadata, capability reporter); custom `[McpTool]` verbs keep the policy (no entity/row → no gate yet). Verified: MCP conformance 59/59, relationship-visibility 2/2, code-mode 27/27, custom-tools 3/3, field-exclusion 5/5; gate-bypass mutation caught by 6 tests; green-ratchet GREEN. the `origin` gate dimension is now IMPLEMENTED (see the extension section below); Phase 5 (P3.1 grants) follows. (The S16.PantryPal dogfood was retired — a single-user anonymous app with nothing to gate; a purpose-built MCP showcase sample is forthcoming.)

1. **Gate** — `AccessGate` bag + `[Access(...)]` attribute + the DNF grammar + a gate provider on the seam (supersedes the entity-wide `[Authorize]`/`[RequireScope]` floor; those remain recognized as gate sugar). Per-action; allow-by-default.
2. **Constrain** — `EntityAccess<T>` realization base + `Owner` once + `Constrain(query, action)` wired through WEB-0068 for reads and through the write/delete paths in `IEntityEndpointService`; `create` stamping.
3. **Project** — the `can` manifest: `Koan-Access` list header (single), opt-in `access` sidecar (collection), default-on MCP metadata. Per-row from gate ∩ `Constrain`.
4. **MCP edge** (ARCH-0092 Phase 3.3) re-pointed at this model; `[McpEntity(RequiredScopes/RequireAuthentication)]` → gate; demote `[McpEntity]`; exercised by the MCP conformance suite (a purpose-built MCP showcase sample is forthcoming — the S16.PantryPal dogfood was retired).
5. **P3.1** grants land on the gate (`AgentGrant` = a relationship `is`/`has`/`owner` resolves against), toolset-grained.

## Extension — `origin` gate dimension (IMPLEMENTED)

A fourth gate term, **`origin: local | internal | remote`**, declared exactly like the others (`[Access(remove: "origin:local, is:admin")]` = removable by a local caller OR an admin). It answers *where the call came from*, which is distinct from *who* the caller is — the transport-trust axis, separate from identity. Realized as a **framework-stamped, server-trusted claim** `koan:origin`: the transport edge stamps it (stripping any client-supplied value, mirroring the create owner-stamp, so it cannot be forged) and `origin:local` is an authoring alias for `has:claim:koan:origin=local`, riding the existing claim machinery and the Phase 3.3 principal thread. The per-row projection honesty extends for free (a remote agent's `can:[]` omits local-only verbs). Tiers: **`local`** = STDIO (OS-guaranteed same-process; the strongest signal), **`remote`** = anything that crossed the perimeter (the safe default), **`internal`** = a trusted network the app **explicitly declares**, **fail-closed** — absent that declaration `internal` never matches, so it can never become a spoofable boundary (the homelab/small-team case: declare the LAN CIDR once and LAN clients are treated "as if local"). This resolves the STDIO posture cleanly: STDIO runs anonymous (the caller is nobody) but `origin:local` lets an author grant the local channel declaratively, per-action.

> **Realized design (shipped on `dev`).** New primitives in `Koan.Web.Authorization`: `OriginTier {Local, Internal, Remote}`, the `Origin` claim vocabulary (`koan:origin`), `OriginStamp.Apply` (strips any client-forged value, then adds the framework tier on a **dedicated unauthenticated carrier identity** so it never changes `IsAuthenticated` — a STDIO caller stays anonymous yet gains `origin:local`), and `OriginOptions.InternalNetworks` (CIDRs, fail-closed) + `OriginResolver` (IP→tier; **never `local`** for a networked caller; loopback HTTP is `remote` unless declared internal). **Key correctness point:** the `origin:` bag is parsed with `Authenticated: false` — origin is orthogonal to identity, so it is the ONE grant term an anonymous principal can satisfy (every other term implies authentication). **Stamping** is single-chokepoint at `EntityRequestContextBuilder.Build`: an HTTP request resolves the tier from `HttpContext.Connection.RemoteIpAddress` + `OriginOptions` (the connection is authoritative, overwriting any forged claim); a non-HTTP (MCP) path pre-stamps at its edge — the STDIO raw handler (`McpRpcHandler.CallTool`) stamps `local`, the HTTP/SSE session (`HttpSseSessionManager`) stamps `remote`/`internal` at open — and `Build` preserves it, defaulting an unstamped non-HTTP path to the safe `remote`. `OriginOptions` binds from `Koan:Web:Origin`. Tested: 21 unit + 6 builder specs (parser, stamp strip/identity-preservation, resolver fail-closed, gate eval) + 3 MCP conformance e2e (STDIO local allowed, remote denied, code-mode held to the gate); `Authenticated:false`→`true` mutation caught by 6 tests. **Deferred nuance:** a denied anonymous-remote caller facing a pure-origin gate currently surfaces `Challenge` (401) rather than `Forbid` (403) — a denial either way; refining `AllBagsNeedAuth` to exclude origin-only bags is a follow-on. **Effective client IP** behind a trusted proxy is the app's `UseForwardedHeaders` responsibility (Koan reads whatever `RemoteIpAddress` resolves to).

## Explicitly deferred / out of scope

- **Secure-by-default opt-in** — a per-app/per-entity switch flipping the default closed; deliberately OFF for delight, revisited if real deployments want a wall.
- **Field-level authorization** — `can` is per-row, per-verb; per-field read/write masking is a later decision.
- **External PDP/ReBAC providers** (OpenFGA/SpiceDB/Oso adapters) — the gate/`Constrain` model is the in-process realization; a relationship-engine rung on the seam (SEC-0002 Tiers 2+) stays future, opt-in.
- **Core-ward hoist of the seam** (for jobs/bus) — ARCH-0092 landed it in `Koan.Web`; deeper hoist deferred to a real consumer.
